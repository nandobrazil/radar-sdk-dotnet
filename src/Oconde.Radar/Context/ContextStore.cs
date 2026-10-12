using System;
#if NETSTANDARD
using System.Threading;
#else
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
#endif

namespace Oconde.Radar.Context
{
    internal static class ContextStore
    {
#if NETSTANDARD
        private static readonly AsyncLocal<RadarContext?> CurrentContext = new AsyncLocal<RadarContext?>();

        public static RadarContext? Current => CurrentContext.Value;

        public static IDisposable Use(RadarContext? context)
        {
            var previous = CurrentContext.Value;
            CurrentContext.Value = context;
            return new Disposer(() => CurrentContext.Value = previous);
        }
#else
        private const string Slot = "Oconde.Radar.Context";

        public static RadarContext? Current
        {
            get
            {
                try
                {
                    return (CallContext.LogicalGetData(Slot) as ObjectHandle)?.Unwrap() as RadarContext;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static IDisposable Use(RadarContext? context)
        {
            var previous = CallContext.LogicalGetData(Slot);
            if (context == null) CallContext.FreeNamedDataSlot(Slot);
            else CallContext.LogicalSetData(Slot, new ObjectHandle(context));
            return new Disposer(() =>
            {
                if (previous == null) CallContext.FreeNamedDataSlot(Slot);
                else CallContext.LogicalSetData(Slot, previous);
            });
        }
#endif
    }
}
