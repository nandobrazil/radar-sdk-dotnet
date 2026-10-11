using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Oconde.Radar.Json
{
    internal sealed class JsonObject : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly List<KeyValuePair<string, object?>> entries = new List<KeyValuePair<string, object?>>();

        public int Count => entries.Count;

        public IEnumerable<string> Keys => entries.Select(entry => entry.Key);

        public object? this[string key]
        {
            get => TryGetValue(key, out var value) ? value : null;
            set => Set(key, value);
        }

        public void Set(string key, object? value)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Key != key) continue;
                entries[index] = new KeyValuePair<string, object?>(key, value);
                return;
            }
            entries.Add(new KeyValuePair<string, object?>(key, value));
        }

        public bool TryGetValue(string key, out object? value)
        {
            foreach (var entry in entries)
            {
                if (entry.Key != key) continue;
                value = entry.Value;
                return true;
            }
            value = null;
            return false;
        }

        public bool ContainsKey(string key) => entries.Any(entry => entry.Key == key);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class JsonArray : List<object?>
    {
    }
}
