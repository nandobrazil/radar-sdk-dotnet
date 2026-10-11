using System;

namespace Oconde.Radar.Transport
{
    internal sealed class TransportSettings
    {
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);
        public int BatchSize { get; set; } = 100;
        public int MaxQueue { get; set; } = 1000;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(1);
    }
}
