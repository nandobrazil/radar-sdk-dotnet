using System;
using System.Globalization;
using System.Text;

namespace Oconde.Radar.Json
{
    internal static class JsonWriter
    {
        public static string Write(object? value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        public static void Write(StringBuilder builder, object? value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case JsonObject record:
                    builder.Append('{');
                    var first = true;
                    foreach (var entry in record)
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        WriteString(builder, entry.Key);
                        builder.Append(':');
                        Write(builder, entry.Value);
                    }
                    builder.Append('}');
                    break;
                case JsonArray items:
                    builder.Append('[');
                    for (var index = 0; index < items.Count; index++)
                    {
                        if (index > 0) builder.Append(',');
                        Write(builder, items[index]);
                    }
                    builder.Append(']');
                    break;
                case double number:
                    builder.Append(FormatNumber(number));
                    break;
                case float number:
                    builder.Append(FormatNumber(number));
                    break;
                case decimal number:
                    builder.Append(FormatNumber((double)number));
                    break;
                case long number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case IConvertible convertible when IsInteger(value):
                    builder.Append(convertible.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(builder, value.ToString() ?? string.Empty);
                    break;
            }
        }

        public static string FormatNumber(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) return "null";
            if (Math.Floor(number) == number && Math.Abs(number) < 1e21) return number.ToString("0", CultureInfo.InvariantCulture);
            var text = number.ToString("R", CultureInfo.InvariantCulture);
            var exponent = text.IndexOfAny(new[] { 'E', 'e' });
            if (exponent < 0) return text;
            var mantissa = text.Substring(0, exponent);
            var power = int.Parse(text.Substring(exponent + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return mantissa + "e" + (power < 0 ? "-" : "+") + Math.Abs(power).ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsInteger(object value) => value is int || value is short || value is byte || value is sbyte || value is ushort || value is uint || value is ulong;

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20 || IsLoneSurrogate(text, index)) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }

        private static bool IsLoneSurrogate(string text, int index)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character)) return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]);
            if (char.IsLowSurrogate(character)) return index == 0 || !char.IsHighSurrogate(text[index - 1]);
            return false;
        }
    }
}
