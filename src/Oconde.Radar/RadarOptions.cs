using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Oconde.Radar
{
    public sealed class RadarOptions
    {
        public string? Key { get; set; }
        public string? Endpoint { get; set; }
        public string? Environment { get; set; }
        public string? Release { get; set; }
        public bool? Enabled { get; set; }
        public bool Console { get; set; }
        public RadarLevel MinLevel { get; set; } = RadarLevel.Info;
        public RedactMode Redact { get; set; } = RedactMode.Mask;
        public RequestDetail RequestDetail { get; set; } = RequestDetail.Full;
        public RadarLogRequests LogRequests { get; set; } = new RadarLogRequests();
        public IList<string> IgnorePaths { get; set; } = new List<string> { "/healthz", "/health" };
        public bool CaptureUnhandled { get; set; } = true;
        public bool Debug { get; set; }
        public bool EnableTls12 { get; set; } = true;
    }

    public sealed class RadarLogRequests
    {
        public bool Enabled { get; set; } = true;
        public IList<string> Include { get; set; } = new List<string>();
        public IList<string> Exclude { get; set; } = new List<string>();
        public IList<Regex> IncludeMatching { get; set; } = new List<Regex>();
        public IList<Regex> ExcludeMatching { get; set; } = new List<Regex>();
        public double Sample { get; set; } = 1.0;
    }
}
