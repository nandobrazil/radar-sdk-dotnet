using System;
using System.Threading;
#if !NET35
using System.Threading.Tasks;
#endif

namespace Oconde.Radar
{
    internal static class UnhandledHooks
    {
        private static int installed;

        public static void Install(RadarClient client)
        {
            if (Interlocked.Exchange(ref installed, 1) == 1) return;
            var reference = new WeakReference(client);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (!(reference.Target is RadarClient target) || !(args.ExceptionObject is Exception error)) return;
                target.CaptureUnhandled(error, true);
                target.Flush(TimeSpan.FromSeconds(2));
            };
#if !NET35
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                if (reference.Target is RadarClient target && args.Exception != null) target.CaptureUnhandled(args.Exception, false);
            };
#endif
        }
    }
}
