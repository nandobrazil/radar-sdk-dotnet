using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Oconde.Radar.Configuration
{
    internal sealed class ResolvedOptions
    {
        public const string DefaultEndpoint = "https://radar-ingest.oconde.dev";

        public string? Key { get; set; }
        public string Endpoint { get; set; } = DefaultEndpoint;
        public string Environment { get; set; } = "production";
        public string? Release { get; set; }
        public bool Enabled { get; set; }
        public bool Console { get; set; }
        public RadarLevel MinLevel { get; set; } = RadarLevel.Info;
        public RedactMode Redact { get; set; } = RedactMode.Mask;
        public RequestDetail RequestDetail { get; set; } = RequestDetail.Full;
        public bool LogRequests { get; set; } = true;
        public string[] Include { get; set; } = new string[0];
        public string[] Exclude { get; set; } = new string[0];
        public Regex[] IncludeMatching { get; set; } = new Regex[0];
        public Regex[] ExcludeMatching { get; set; } = new Regex[0];
        public double Sample { get; set; } = 1.0;
        public HashSet<string> IgnorePaths { get; set; } = new HashSet<string> { "/healthz", "/health" };
        public bool CaptureUnhandled { get; set; } = true;
        public bool Debug { get; set; }
        public bool EnableTls12 { get; set; } = true;
    }
}
