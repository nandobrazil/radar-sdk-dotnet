using System;
using System.Collections.Generic;
using System.Globalization;
#if !NET48
using System.Threading.Tasks;
#endif

namespace Oconde.Radar.Tests
{
    public static class SampleErrors
    {
        public sealed class Checkout
        {
            public int Total(Dictionary<string, int> order)
            {
                return order["total"];
            }
        }

        public sealed class BrokenMessageException : Exception
        {
            public override string Message => throw new InvalidOperationException("no message");
        }

        public static void LoadOrder() => new Checkout().Total(new Dictionary<string, int>());

        public static void Wrapped()
        {
            try
            {
                LoadOrder();
            }
            catch (KeyNotFoundException error)
            {
                throw new InvalidOperationException("pedido sem total", error);
            }
        }

        public static void Recurse(int depth)
        {
            if (depth == 0) throw new InvalidOperationException("fundo");
            Recurse(depth - 1);
        }

        public static void Lambda()
        {
            Action action = () => LoadOrder();
            action();
        }

        public static int ParseInvalid() => int.Parse("not a number", CultureInfo.InvariantCulture);

#if !NET48
        public static async Task LoadOrderAsync()
        {
            await Task.Yield();
            LoadOrder();
        }
#endif

        public static Exception Caught(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                return error;
            }
            throw new InvalidOperationException("expected an exception");
        }
    }
}
