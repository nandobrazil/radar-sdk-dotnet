using System.Text;
using Oconde.Radar.Json;
using Oconde.Radar.Redaction;

namespace Oconde.Radar.Serialization
{
    internal static class Attributes
    {
        public static JsonObject Prepare(object? data, RedactMode mode)
        {
            var clean = Redactor.RedactDeep(Sanitizer.Sanitize(data), mode);
            var record = clean as JsonObject;
            if (record == null)
            {
                record = new JsonObject();
                record.Set("value", clean);
            }
            return LimitBytes(record, Protocol.AttributesBytes);
        }

        public static JsonObject LimitBytes(JsonObject record, int maxBytes)
        {
            var json = JsonWriter.Write(record);
            if (Encoding.UTF8.GetByteCount(json) <= maxBytes) return record;
            var truncated = new JsonObject();
            truncated.Set("_truncated", Text.Truncate(json, maxBytes));
            return truncated;
        }
    }
}
