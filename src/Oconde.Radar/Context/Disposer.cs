using System;
using System.Threading;

namespace Oconde.Radar.Context
{
    internal sealed class Disposer : IDisposable
    {
        private Action? action;

        public Disposer(Action action)
        {
            this.action = action;
        }

        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
