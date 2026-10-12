using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Oconde.Radar.Json;

namespace Oconde.Radar.Transport
{
    internal sealed class TransportTarget
    {
        public string EventsUrl { get; set; } = string.Empty;
        public string CheckInBaseUrl { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string SdkName { get; set; } = string.Empty;
        public string SdkVersion { get; set; } = string.Empty;
        public string Environment { get; set; } = "production";
        public string? Release { get; set; }
    }

    internal sealed class EventTransport
    {
        public const int MaxBatchBytes = 900_000;
        public const int GzipThresholdBytes = 8192;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan UnauthorizedPause = TimeSpan.FromMinutes(10);

        [ThreadStatic]
        private static bool transportThread;

        private readonly TransportTarget target;
        private readonly TransportSettings settings;
        private readonly IHttpSender sender;
        private readonly Action<string, string, bool> warn;
        private readonly object gate = new object();
        private readonly LinkedList<string> queue = new LinkedList<string>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private int dropped;
        private bool inFlight;
        private bool stopped;
        private TimeSpan nextAttemptAt = TimeSpan.Zero;
        private TimeSpan unauthorizedUntil = TimeSpan.Zero;
        private TimeSpan backoff = TimeSpan.Zero;

        public EventTransport(TransportTarget target, TransportSettings settings, IHttpSender sender, Action<string, string, bool> warn)
        {
            this.target = target;
            this.settings = settings;
            this.sender = sender;
            this.warn = warn;
            new Thread(Run) { IsBackground = true, Name = "oconde-radar" }.Start();
        }

        public static bool IsTransportThread => transportThread;

        public int Pending
        {
            get
            {
                lock (gate) return queue.Count;
            }
        }

        public void Enqueue(string json)
        {
            var full = false;
            lock (gate)
            {
                if (stopped) return;
                if (queue.Count >= settings.MaxQueue)
                {
                    dropped++;
                    full = true;
                }
                else
                {
                    queue.AddLast(json);
                    if (queue.Count >= settings.BatchSize) Monitor.PulseAll(gate);
                }
            }
            if (full) warn("queue_full", "fila do Radar cheia; eventos estão sendo descartados", false);
        }

        public bool Flush(TimeSpan timeout)
        {
            var deadline = clock.Elapsed + timeout;
            while (true)
            {
                lock (gate)
                {
                    if (queue.Count == 0 && !inFlight) return true;
                    if (inFlight)
                    {
                        var waiting = deadline - clock.Elapsed;
                        if (waiting <= TimeSpan.Zero) return false;
                        Monitor.Wait(gate, waiting);
                        continue;
                    }
                }
                var remaining = deadline - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) return false;
                if (SendOnce(remaining < settings.Timeout ? remaining : settings.Timeout)) continue;
                lock (gate)
                {
                    if (!inFlight) return queue.Count == 0;
                }
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                Monitor.PulseAll(gate);
            }
        }

        public void CheckIn(string slug, string json, TimeSpan timeout)
        {
            lock (gate)
            {
                if (stopped || clock.Elapsed < unauthorizedUntil) return;
            }
            var outcome = sender.Post(target.CheckInBaseUrl + Uri.EscapeDataString(slug), Encoding.UTF8.GetBytes(json), false, Headers(), timeout);
            if (outcome.Status >= 200 && outcome.Status < 300) return;
            if (outcome.Status == 401 || outcome.Status == 403) PauseUnauthorized(outcome.Status);
            else if (outcome.Status == 404) warn("checkin_unknown_" + slug, "tarefa agendada \"" + slug + "\" não existe no projeto da chave; crie o monitor com esse slug no Radar", true);
            else if (outcome.Status == 0) warn("checkin_network", "check-in no Radar falhou: " + outcome.Error, false);
            else warn("checkin_rejected_" + outcome.Status, "check-in recusado pelo Radar (" + outcome.Status + ")", false);
        }

        private void Run()
        {
            transportThread = true;
            while (true)
            {
                lock (gate)
                {
                    if (stopped) return;
                    if (queue.Count < settings.BatchSize) Monitor.Wait(gate, settings.FlushInterval);
                    if (stopped) return;
                }
                try
                {
                    SendOnce(settings.Timeout);
                }
                catch (Exception error)
                {
                    warn("internal", "envio ao Radar falhou: " + error.Message, false);
                }
            }
        }

        private bool SendOnce(TimeSpan timeout)
        {
            List<string> items;
            lock (gate)
            {
                if (inFlight || stopped || queue.Count == 0 || clock.Elapsed < nextAttemptAt) return false;
                items = new List<string>();
                while (items.Count < settings.BatchSize && queue.Count > 0)
                {
                    items.Add(queue.First!.Value);
                    queue.RemoveFirst();
                }
                inFlight = true;
            }
            try
            {
                return SendBatch(items, timeout);
            }
            finally
            {
                lock (gate)
                {
                    inFlight = false;
                    Monitor.PulseAll(gate);
                }
            }
        }

        private bool SendBatch(List<string> items, TimeSpan timeout)
        {
            var events = new List<string>();
            var leftovers = new List<string>();
            var size = 0;
            foreach (var item in items)
            {
                var length = Encoding.UTF8.GetByteCount(item);
                if (events.Count > 0 && size + length > MaxBatchBytes)
                {
                    leftovers.Add(item);
                    continue;
                }
                events.Add(item);
                size += length;
            }
            int droppedSnapshot;
            lock (gate)
            {
                for (var index = leftovers.Count - 1; index >= 0; index--) queue.AddFirst(leftovers[index]);
                droppedSnapshot = dropped;
            }
            var body = Encoding.UTF8.GetBytes(Envelope(events, droppedSnapshot));
            var gzip = body.Length > GzipThresholdBytes;
            var outcome = sender.Post(target.EventsUrl, gzip ? Compress(body) : body, gzip, Headers(), timeout);
            if (outcome.Status >= 200 && outcome.Status < 300)
            {
                lock (gate)
                {
                    dropped -= droppedSnapshot;
                    backoff = TimeSpan.Zero;
                    nextAttemptAt = TimeSpan.Zero;
                }
                return true;
            }
            if (outcome.Status == 401 || outcome.Status == 403)
            {
                lock (gate) dropped += events.Count;
                PauseUnauthorized(outcome.Status);
                return false;
            }
            if (outcome.Status == 0 || outcome.Status == 429 || outcome.Status >= 500)
            {
                if (outcome.Status == 0) warn("network", "Radar indisponível: " + outcome.Error, false);
                lock (gate)
                {
                    for (var index = events.Count - 1; index >= 0; index--) queue.AddFirst(events[index]);
                    while (queue.Count > settings.MaxQueue)
                    {
                        queue.RemoveLast();
                        dropped++;
                    }
                    backoff = backoff == TimeSpan.Zero ? settings.BackoffBase : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    var retryAfter = outcome.RetryAfterSeconds.HasValue ? TimeSpan.FromSeconds(outcome.RetryAfterSeconds.Value) : TimeSpan.Zero;
                    nextAttemptAt = clock.Elapsed + (retryAfter > backoff ? retryAfter : backoff);
                }
                return false;
            }
            lock (gate) dropped += events.Count;
            warn("rejected_" + outcome.Status, "lote recusado pelo Radar (" + outcome.Status + "); eventos descartados", true);
            return true;
        }

        private void PauseUnauthorized(int status)
        {
            lock (gate)
            {
                dropped += queue.Count;
                queue.Clear();
                unauthorizedUntil = clock.Elapsed + UnauthorizedPause;
                if (nextAttemptAt < unauthorizedUntil) nextAttemptAt = unauthorizedUntil;
            }
            warn("unauthorized", "chave do Radar recusada (" + status + "); envios pausados por 10 minutos", true);
        }

        private string Envelope(List<string> events, int droppedCount)
        {
            var builder = new StringBuilder();
            builder.Append("{\"v\":").Append(Protocol.Version);
            var sdk = new JsonObject();
            sdk.Set("name", target.SdkName);
            sdk.Set("version", target.SdkVersion);
            builder.Append(",\"sdk\":");
            JsonWriter.Write(builder, sdk);
            builder.Append(",\"environment\":");
            JsonWriter.Write(builder, target.Environment);
            if (target.Release != null)
            {
                builder.Append(",\"release\":");
                JsonWriter.Write(builder, target.Release);
            }
            if (droppedCount > 0) builder.Append(",\"dropped\":").Append(droppedCount);
            builder.Append(",\"events\":[").Append(string.Join(",", events.ToArray())).Append("]}");
            return builder.ToString();
        }

        private Dictionary<string, string> Headers() => new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + target.Key,
            ["User-Agent"] = target.SdkName + "/" + target.SdkVersion,
        };

        private static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress)) gzip.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }
    }
}
