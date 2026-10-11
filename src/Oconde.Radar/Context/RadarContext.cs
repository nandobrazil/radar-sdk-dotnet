using System;
using System.Diagnostics;
using Oconde.Radar.Integration;
using Oconde.Radar.Json;

namespace Oconde.Radar.Context
{
    internal sealed class RadarContext
    {
        public RadarContext(string requestId, RadarRequest? request = null)
        {
            RequestId = requestId;
            Started = Stopwatch.StartNew();
            Request = request;
        }

        public string RequestId { get; }
        public Stopwatch Started { get; }
        public RadarRequest? Request { get; }
        public string? Route { get; set; }
        public JsonObject? User { get; set; }

        public long ElapsedMs => (long)Math.Round(Started.Elapsed.TotalMilliseconds);
    }
}
