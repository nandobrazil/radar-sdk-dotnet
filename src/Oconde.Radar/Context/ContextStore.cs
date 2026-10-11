using System;
#if NETSTANDARD
using System.Threading;
#else
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

        [Serializable]
        private sealed class Holder
        {
            [NonSerialized]
            public RadarContext? Value;
        }

        public static RadarContext? Current => (CallContext.LogicalGetData(Slot) as Holder)?.Value;

        public static IDisposable Use(RadarContext? context)
        {
            var previous = CallContext.LogicalGetData(Slot);
            CallContext.LogicalSetData(Slot, new Holder { Value = context });
            return new Disposer(() =>
            {
                if (previous == null) CallContext.FreeNamedDataSlot(Slot);
                else CallContext.LogicalSetData(Slot, previous);
            });
        }
#endif
    }
}
