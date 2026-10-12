using System;
using System.Collections.Generic;

namespace Oconde.Radar.Integration
{
    public sealed class RadarRequest
    {
        public RadarRequest(string method, string path)
        {
            Method = string.IsNullOrEmpty(method) ? "GET" : method;
            Path = string.IsNullOrEmpty(path) ? "/" : path;
        }

        public string Method { get; }
        public string Path { get; }
        public string QueryString { get; set; } = string.Empty;
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Func<string?>? ClientIp { get; set; }
        public Func<IDictionary<string, string>?>? RouteValues { get; set; }
        public Func<string?>? RoutePattern { get; set; }
    }
}
