using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
#if NETFRAMEWORK
using System.Net;
#endif
#if !NET35
using System.Threading.Tasks;
#endif
using Oconde.Radar.Configuration;
using Oconde.Radar.Context;
using Oconde.Radar.Json;
using Oconde.Radar.Redaction;
using Oconde.Radar.Runtime;
using Oconde.Radar.Serialization;
using Oconde.Radar.Stack;
using Oconde.Radar.Transport;

namespace Oconde.Radar
{
    public sealed class RadarClient : IDisposable
    {
        private const string CapturedMark = "Oconde.Radar.Captured";
        private const long MaxCheckInDurationMs = 7L * 86_400_000;
        private static readonly TimeSpan ExitFlush = TimeSpan.FromSeconds(2);
        private readonly object gate = new object();
        private readonly HashSet<string> warned = new HashSet<string>();
        private ResolvedOptions options = OptionsResolver.Inactive();
        private EventTransport? transport;
        private TimeSpan checkInTimeout = TimeSpan.FromSeconds(5);
        private bool initialized;
        private volatile bool closed;

        public bool IsEnabled => transport != null && !closed;

        internal bool IsActive => !closed && (transport != null || options.Console);

        internal ResolvedOptions Settings => options;

        public void Init(RadarOptions? options = null) => Init(options, new TransportSettings());

        internal void Init(RadarOptions? options, TransportSettings settings)
        {
            try
            {
                lock (gate)
                {
                    if (initialized)
                    {
                        DebugWarn("Radar.Init chamado mais de uma vez; mantida a primeira configuração");
                        return;
                    }
                    initialized = true;
                }
                var resolved = OptionsResolver.Resolve(options);
                this.options = resolved;
                Redactor.SetFingerprintSecret(resolved.Key);
                if (resolved.Enabled && resolved.Key != null)
                {
                    EnableTls12(resolved);
                    checkInTimeout = settings.Timeout;
                    transport = new EventTransport(
                        new TransportTarget
                        {
                            EventsUrl = resolved.Endpoint + Protocol.IngestPath,
                            CheckInBaseUrl = resolved.Endpoint + Protocol.CheckInPath,
                            Key = resolved.Key,
                            SdkName = SdkInfo.Name,
                            SdkVersion = SdkInfo.Version,
                            Environment = resolved.Environment,
                            Release = resolved.Release,
                        },
                        settings,
                        CreateSender(),
                        (key, message, always) =>
                        {
                            if (always) WarnOnce(key, message);
                            else DebugWarn(message);
                        });
                    AppDomain.CurrentDomain.ProcessExit += (sender, args) => Flush(ExitFlush);
                    AppDomain.CurrentDomain.DomainUnload += (sender, args) => Flush(ExitFlush);
                }
                if (resolved.CaptureUnhandled && IsActive) UnhandledHooks.Install(this);
            }
            catch (Exception error)
            {
                WriteWarning("Radar.Init falhou: " + error.Message);
            }
        }

        public void Debug(string message, object? attributes = null) => Log(RadarLevel.Debug, message, attributes);

        public void Info(string message, object? attributes = null) => Log(RadarLevel.Info, message, attributes);

        public void Warn(string message, object? attributes = null) => Log(RadarLevel.Warn, message, attributes);

        public void Error(string message, object? attributes = null) => Log(RadarLevel.Error, message, attributes);

        public void Log(RadarLevel level, string message, object? attributes = null) => Safely(() => EmitLog(level, message, attributes, null, null));

        public void CaptureException(Exception exception, object? attributes = null) => Safely(() => EmitError(exception, attributes, "error", true, null));

        public void SetUser(string? id = null, string? email = null, string? name = null) => Safely(() =>
        {
            var context = ContextStore.Current;
            if (context == null) return;
            var user = new JsonObject();
            if (!Text.IsBlank(id)) user.Set("id", Text.Truncate(id!, 200));
            if (!Text.IsBlank(email)) user.Set("email", Text.Truncate(email!, 200));
            if (!Text.IsBlank(name)) user.Set("name", Text.Truncate(name!, 200));
            context.User = user.Count > 0 ? user : null;
        });

        public RadarScope StartScope(string? requestId = null)
        {
            try
            {
                var context = new RadarContext(RequestIds.From(requestId));
                return new RadarScope(context.RequestId, ContextStore.Use(context));
            }
            catch (Exception)
            {
                return new RadarScope(RequestIds.From(requestId), new Disposer(() => { }));
            }
        }

        public T Track<T>(string name, Func<T> work, object? attributes = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            var scope = ContextStore.Current == null ? StartScope() : null;
            var watch = Stopwatch.StartNew();
            try
            {
                var result = work();
                Log(RadarLevel.Info, name, Outcome(attributes, watch, true));
                return result;
            }
            catch (Exception error)
            {
                Log(RadarLevel.Error, name, Outcome(attributes, watch, false));
                CaptureException(error, attributes);
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public void Track(string name, Action work, object? attributes = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            Track<object?>(name, () =>
            {
                work();
                return null;
            }, attributes);
        }

        public void CheckIn(string slug, CheckInStatus status, string? checkInId = null, double? durationMs = null) => Safely(() =>
        {
            var target = transport;
            if (target == null || closed) return;
            if (slug == null || !Protocol.CheckInSlug.IsMatch(slug))
            {
                DebugWarn("slug de tarefa agendada inválido: " + Text.Truncate(slug ?? string.Empty, 80));
                return;
            }
            var payload = new JsonObject();
            payload.Set("status", status == CheckInStatus.InProgress ? "in_progress" : status == CheckInStatus.Ok ? "ok" : "error");
            if (!string.IsNullOrEmpty(checkInId)) payload.Set("checkInId", checkInId!.Length > Protocol.CheckInIdLength ? checkInId.Substring(0, Protocol.CheckInIdLength) : checkInId);
            if (durationMs.HasValue && !double.IsNaN(durationMs.Value) && !double.IsInfinity(durationMs.Value) && durationMs.Value >= 0) payload.Set("durationMs", Math.Min((long)Math.Round(durationMs.Value), MaxCheckInDurationMs));
            payload.Set("environment", options.Environment.Length > 64 ? options.Environment.Substring(0, 64) : options.Environment);
            if (options.Release != null) payload.Set("release", options.Release.Length > 128 ? options.Release.Substring(0, 128) : options.Release);
            target.CheckIn(slug, JsonWriter.Write(payload), checkInTimeout);
        });

        public T RunJob<T>(string slug, Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            var checkInId = Guid.NewGuid().ToString();
            var scope = ContextStore.Current == null ? StartScope() : null;
            var watch = Stopwatch.StartNew();
            CheckIn(slug, CheckInStatus.InProgress, checkInId);
            try
            {
                var result = work();
                CheckIn(slug, CheckInStatus.Ok, checkInId, watch.Elapsed.TotalMilliseconds);
                return result;
            }
            catch (Exception error)
            {
                CaptureException(error, new Dictionary<string, object> { ["cron"] = slug });
                CheckIn(slug, CheckInStatus.Error, checkInId, watch.Elapsed.TotalMilliseconds);
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public void RunJob(string slug, Action work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            RunJob<object?>(slug, () =>
            {
                work();
                return null;
            });
        }

#if !NET35
        public async Task<T> TrackAsync<T>(string name, Func<Task<T>> work, object? attributes = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            var scope = ContextStore.Current == null ? StartScope() : null;
            var watch = Stopwatch.StartNew();
            try
            {
                var result = await work().ConfigureAwait(false);
                Log(RadarLevel.Info, name, Outcome(attributes, watch, true));
                return result;
            }
            catch (Exception error)
            {
                Log(RadarLevel.Error, name, Outcome(attributes, watch, false));
                CaptureException(error, attributes);
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public Task TrackAsync(string name, Func<Task> work, object? attributes = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return TrackAsync<object?>(name, async () =>
            {
                await work().ConfigureAwait(false);
                return null;
            }, attributes);
        }

        public async Task<T> RunJobAsync<T>(string slug, Func<Task<T>> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            var checkInId = Guid.NewGuid().ToString();
            var scope = ContextStore.Current == null ? StartScope() : null;
            var watch = Stopwatch.StartNew();
            await Task.Run(() => CheckIn(slug, CheckInStatus.InProgress, checkInId)).ConfigureAwait(false);
            try
            {
                var result = await work().ConfigureAwait(false);
                await Task.Run(() => CheckIn(slug, CheckInStatus.Ok, checkInId, watch.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
                return result;
            }
            catch (Exception error)
            {
                CaptureException(error, new Dictionary<string, object> { ["cron"] = slug });
                await Task.Run(() => CheckIn(slug, CheckInStatus.Error, checkInId, watch.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public Task RunJobAsync(string slug, Func<Task> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return RunJobAsync<object?>(slug, async () =>
            {
                await work().ConfigureAwait(false);
                return null;
            });
        }
#endif

        public bool Flush(TimeSpan timeout)
        {
            try
            {
                return transport?.Flush(timeout) ?? true;
            }
            catch (Exception error)
            {
                DebugWarn("flush falhou: " + error.Message);
                return false;
            }
        }

        public void Close(TimeSpan timeout)
        {
            if (closed) return;
            Flush(timeout);
            closed = true;
            transport?.Stop();
        }

        public void Dispose() => Close(ExitFlush);

        internal void CaptureUnhandled(Exception exception, bool fatal) => Safely(() => EmitError(exception, null, fatal ? "fatal" : "error", false, null));

        internal void EmitLog(RadarLevel level, string? message, object? attributes, JsonObject? request, RedactMode? mode)
        {
            if (closed || level < options.MinLevel) return;
            var context = ContextStore.Current;
            var record = new JsonObject();
            record.Set("type", "log");
            record.Set("ts", NowMs());
            record.Set("level", LevelName(level));
            record.Set("message", Text.Truncate(message ?? string.Empty, Protocol.MessageLength));
            if (context != null) record.Set("requestId", context.RequestId);
            if (attributes != null) record.Set("attrs", Attributes.Prepare(attributes, mode ?? options.Redact));
            if (request != null) record.Set("request", request);
            if (context?.User != null) record.Set("user", context.User);
            Deliver(record);
        }

        internal void EmitError(Exception exception, object? attributes, string level, bool handled, RadarContext? contextOverride)
        {
            if (closed || exception == null || AlreadyCaptured(exception)) return;
            var context = contextOverride ?? ContextStore.Current;
            var record = new JsonObject();
            record.Set("type", "error");
            record.Set("ts", NowMs());
            record.Set("level", level);
            record.Set("handled", handled);
            record.Set("exception", ExceptionInfo.Describe(exception));
            if (context != null)
            {
                record.Set("requestId", context.RequestId);
                if (context.User != null) record.Set("user", context.User);
            }
            record.Set("runtime", RuntimeInfo.Describe());
            if (attributes != null) record.Set("attrs", Attributes.Prepare(attributes, options.Redact));
            Deliver(record);
        }

        private void Deliver(JsonObject record)
        {
            if (options.Console) Print(record);
            transport?.Enqueue(JsonWriter.Write(record));
        }

        private void Print(JsonObject record)
        {
            try
            {
                var line = new JsonObject();
                if (record["attrs"] is JsonObject attrs)
                {
                    foreach (var entry in attrs) line.Set(entry.Key, entry.Value);
                }
                var ts = (long)record["ts"]!;
                line.Set("ts", new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                line.Set("level", record["level"]);
                var isError = (string?)record["type"] == "error";
                if (isError)
                {
                    var exception = (JsonObject)record["exception"]!;
                    line.Set("message", exception["type"] + ": " + exception["message"]);
                }
                else
                {
                    line.Set("message", record["message"]);
                }
                if (record.TryGetValue("requestId", out var requestId)) line.Set("requestId", requestId);
                var stream = isError || (string?)record["level"] == "error" ? System.Console.Error : System.Console.Out;
                stream.WriteLine(JsonWriter.Write(line));
            }
            catch (Exception)
            {
            }
        }

        private bool AlreadyCaptured(Exception exception)
        {
            lock (gate)
            {
                try
                {
                    if (exception.Data.Contains(CapturedMark)) return true;
                    exception.Data[CapturedMark] = true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private static JsonObject Outcome(object? attributes, Stopwatch watch, bool ok)
        {
            var record = Sanitizer.Sanitize(attributes) as JsonObject;
            if (record == null)
            {
                var value = Sanitizer.Sanitize(attributes);
                record = new JsonObject();
                if (value != null) record.Set("value", value);
            }
            record.Set("durationMs", (long)Math.Round(watch.Elapsed.TotalMilliseconds));
            record.Set("ok", ok);
            return record;
        }

        private static long NowMs() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        private static string LevelName(RadarLevel level) => level == RadarLevel.Debug ? "debug" : level == RadarLevel.Info ? "info" : level == RadarLevel.Warn ? "warn" : "error";

        private static IHttpSender CreateSender()
        {
#if NETFRAMEWORK
            return new WebRequestSender();
#else
            return new HttpClientSender();
#endif
        }

        private void EnableTls12(ResolvedOptions resolved)
        {
#if NETFRAMEWORK
            if (!resolved.EnableTls12) return;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception)
            {
                WriteWarning("TLS 1.2 indisponível neste .NET Framework; atualize o Windows ou o .NET para o Radar receber os eventos");
            }
#endif
        }

        private void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                DebugWarn("falha interna do Radar: " + error.Message);
            }
        }

        private void WarnOnce(string key, string message)
        {
            lock (warned)
            {
                if (!warned.Add(key)) return;
            }
            WriteWarning(message);
        }

        internal void DebugWarn(string message)
        {
            if (options.Debug) WriteWarning(message);
        }

        private static void WriteWarning(string message)
        {
            try
            {
                System.Console.Error.WriteLine("[radar] " + message);
            }
            catch (Exception)
            {
            }
        }
    }
}
