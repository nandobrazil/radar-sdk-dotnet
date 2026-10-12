using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Oconde.Radar.Extensions.Logging
{
    [ProviderAlias("Radar")]
    public sealed class RadarLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly RadarClient client;
        private readonly ConcurrentDictionary<string, RadarLogger> loggers = new ConcurrentDictionary<string, RadarLogger>(StringComparer.Ordinal);
        private IExternalScopeProvider? scopes;

        public RadarLoggerProvider(RadarClient? client = null)
        {
            this.client = client ?? global::Oconde.Radar.Radar.Client;
        }

        internal IExternalScopeProvider? Scopes => scopes;

        public ILogger CreateLogger(string categoryName) => loggers.GetOrAdd(categoryName ?? string.Empty, name => new RadarLogger(name, client, this));

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

        public void Dispose()
        {
        }
    }
}
