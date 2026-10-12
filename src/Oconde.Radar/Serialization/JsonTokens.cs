using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Oconde.Radar.Serialization
{
    internal static class JsonTokens
    {
        private const string NewtonsoftValue = "Newtonsoft.Json.Linq.JValue";
        private const string NewtonsoftObject = "Newtonsoft.Json.Linq.JObject";
        private const string SystemElement = "System.Text.Json.JsonElement";
        private const string SystemNodeValue = "System.Text.Json.Nodes.JsonValue";
        private const int MaxProperties = 200;

        public static bool TryRead(object value, out object? plain)
        {
            plain = null;
            var type = value.GetType();
            if (Inherits(type, NewtonsoftValue))
            {
                plain = type.GetProperty("Value")?.GetValue(value, null);
                return true;
            }
            if (Inherits(type, NewtonsoftObject))
            {
                plain = NewtonsoftProperties((IEnumerable)value);
                return true;
            }
            if (type.FullName == SystemElement)
            {
                plain = Element(value, 0);
                return true;
            }
            if (Inherits(type, SystemNodeValue))
            {
                plain = NodeValue(value, type);
                return true;
            }
            return false;
        }

        private static bool Inherits(Type type, string fullName)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                if (current.FullName == fullName) return true;
            }
            return false;
        }

        private static Dictionary<string, object?> NewtonsoftProperties(IEnumerable properties)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                if (result.Count >= MaxProperties)
                {
                    result["…"] = Sanitizer.LimitReached;
                    break;
                }
                var type = property.GetType();
                var name = type.GetProperty("Name")?.GetValue(property, null) as string;
                if (name != null) result[name] = type.GetProperty("Value")?.GetValue(property, null);
            }
            return result;
        }

        private static object? Element(object element, int depth)
        {
            var type = element.GetType();
            switch (type.GetProperty("ValueKind")?.GetValue(element, null)?.ToString())
            {
                case "Object":
                    return depth > Protocol.MaxDepth ? Sanitizer.TooDeep : Properties(element, type, depth);
                case "Array":
                    return depth > Protocol.MaxDepth ? Sanitizer.TooDeep : Items(element, type, depth);
                case "String":
                    return type.GetMethod("GetString", Type.EmptyTypes)?.Invoke(element, null);
                case "Number":
                    return Number(type.GetMethod("GetRawText", Type.EmptyTypes)?.Invoke(element, null) as string);
                case "True":
                    return true;
                case "False":
                    return false;
                default:
                    return null;
            }
        }

        private static Dictionary<string, object?> Properties(object element, Type type, int depth)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            var properties = type.GetMethod("EnumerateObject", Type.EmptyTypes)?.Invoke(element, null) as IEnumerable;
            if (properties == null) return result;
            foreach (var property in properties)
            {
                if (result.Count >= MaxProperties)
                {
                    result["…"] = Sanitizer.LimitReached;
                    break;
                }
                var propertyType = property.GetType();
                var name = propertyType.GetProperty("Name")?.GetValue(property, null) as string ?? string.Empty;
                var value = propertyType.GetProperty("Value")?.GetValue(property, null);
                result[name] = value == null ? null : Element(value, depth + 1);
            }
            return result;
        }

        private static List<object?> Items(object element, Type type, int depth)
        {
            var result = new List<object?>();
            var items = type.GetMethod("EnumerateArray", Type.EmptyTypes)?.Invoke(element, null) as IEnumerable;
            if (items == null) return result;
            foreach (var item in items)
            {
                if (result.Count >= Protocol.ArrayItems)
                {
                    result.Add("…[mais]");
                    break;
                }
                result.Add(item == null ? null : Element(item, depth + 1));
            }
            return result;
        }

        private static object? NodeValue(object node, Type type)
        {
            var json = type.GetMethod("ToJsonString")?.Invoke(node, new object?[] { null }) as string;
            if (json == null) return node.ToString();
            if (json.StartsWith("\"", StringComparison.Ordinal)) return node.ToString();
            if (json == "true") return true;
            if (json == "false") return false;
            if (json == "null") return null;
            return Number(json);
        }

        private static object? Number(string? text)
        {
            if (text == null) return null;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return integer;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
            return text;
        }
    }
}
