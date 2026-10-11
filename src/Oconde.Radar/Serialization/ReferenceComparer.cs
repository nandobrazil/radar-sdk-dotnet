using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Oconde.Radar.Serialization
{
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object? first, object? second) => ReferenceEquals(first, second);

        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
