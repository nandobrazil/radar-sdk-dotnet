using System;

namespace Oconde.Radar
{
    public sealed class RadarScope : IDisposable
    {
        private readonly IDisposable restore;

        internal RadarScope(string requestId, IDisposable restore)
        {
            RequestId = requestId;
            this.restore = restore;
        }

        public string RequestId { get; }

        public void Dispose() => restore.Dispose();
    }
}
