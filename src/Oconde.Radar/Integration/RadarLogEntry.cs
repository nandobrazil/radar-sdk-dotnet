using System;
using System.Collections.Generic;

namespace Oconde.Radar.Integration
{
    public sealed class RadarLogEntry
    {
        public RadarLogEntry(RadarLevel level, string? message)
        {
            Level = level;
            Message = message ?? string.Empty;
        }

        public RadarLevel Level { get; }
        public string Message { get; }
        public string? Logger { get; set; }
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
        public Exception? Exception { get; set; }
        public bool Unhandled { get; set; }
    }
}
