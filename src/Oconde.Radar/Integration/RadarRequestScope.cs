using System;
using System.Threading;
using Oconde.Radar.Context;

namespace Oconde.Radar.Integration
{
    public sealed class RadarRequestScope : IDisposable
    {
        private readonly RadarClient client;
        private readonly RadarContext context;
        private readonly IDisposable restore;
        private int finished;

        internal RadarRequestScope(RadarClient client, RadarContext context, IDisposable restore)
        {
            this.client = client;
            this.context = context;
            this.restore = restore;
        }

        public string RequestId => context.RequestId;

        public void SetRoute(string? route)
        {
            var normalized = RequestInfo.NormalizeRoute(route);
            if (normalized != null) context.Route = normalized;
        }

        public void CaptureException(Exception error) => client.CaptureRequestException(error, context);

        public void Finish(int status)
        {
            if (Interlocked.Exchange(ref finished, 1) == 1) return;
            client.FinishRequest(context, status);
        }

        public void Dispose()
        {
            RequestInfo.Freeze(context);
            restore.Dispose();
        }
    }
}
