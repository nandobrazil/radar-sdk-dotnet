using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Oconde.Radar.Integration;

namespace Oconde.Radar.Extensions.Logging
{
    internal sealed class RadarLogger : ILogger
    {
        private const string OriginalFormat = "{OriginalFormat}";
        private const string ScopeKey = "scope";
        private static readonly HashSet<string> HostingScopeKeys = new HashSet<string>(StringComparer.Ordinal) { "RequestId", "RequestPath", "ConnectionId", "SpanId", "TraceId", "ParentId" };
        private static readonly string[] UnhandledCategories = { "Microsoft.AspNetCore.Diagnostics.", "Microsoft.AspNetCore.Server.", "Microsoft.Extensions.Hosting.Internal.Host" };

        private readonly string category;
        private readonly RadarClient client;
        private readonly RadarLoggerProvider provider;

        public RadarLogger(string category, RadarClient client, RadarLoggerProvider provider)
        {
            this.category = category;
            this.client = client;
            this.provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state) => provider.Scopes?.Push(state) ?? NoScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && client.IsLevelEnabled(ToRadarLevel(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                var entry = new RadarLogEntry(ToRadarLevel(logLevel), Render(state, exception, formatter))
                {
                    Logger = category,
                    Exception = exception,
                    Unhandled = exception != null && IsUnhandledCategory(category),
                };
                provider.Scopes?.ForEachScope((scope, properties) => AddScope(scope, properties), entry.Properties);
                AddState(state, entry.Properties);
                client.Write(entry);
            }
            catch (Exception)
            {
            }
        }

        internal static RadarLevel ToRadarLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return RadarLevel.Debug;
                case LogLevel.Information:
                    return RadarLevel.Info;
                case LogLevel.Warning:
                    return RadarLevel.Warn;
                default:
                    return RadarLevel.Error;
            }
        }

        private static string Render<TState>(TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            try
            {
                return formatter?.Invoke(state, exception) ?? state?.ToString() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsUnhandledCategory(string category)
        {
            foreach (var prefix in UnhandledCategories)
            {
                if (category.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void AddState<TState>(TState state, IDictionary<string, object?> properties)
        {
            if (!(state is IEnumerable<KeyValuePair<string, object>> pairs)) return;
            foreach (var pair in pairs)
            {
                if (pair.Key == OriginalFormat) continue;
                properties[pair.Key] = pair.Value;
            }
        }

        private static void AddScope(object scope, IDictionary<string, object?> properties)
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key == OriginalFormat || HostingScopeKeys.Contains(pair.Key)) continue;
                    properties[pair.Key] = pair.Value;
                }
                return;
            }
            if (scope == null) return;
            if (!(properties.TryGetValue(ScopeKey, out var existing) && existing is List<object> list))
            {
                list = new List<object>();
                properties[ScopeKey] = list;
            }
            list.Add(scope);
        }

        private sealed class NoScope : IDisposable
        {
            public static readonly NoScope Instance = new NoScope();

            public void Dispose()
            {
            }
        }
    }
}
