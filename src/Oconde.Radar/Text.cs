namespace Oconde.Radar
{
    internal static class Text
    {
        public const string Truncated = "…[cortado]";

        public static string Truncate(string value, int maxLength = Protocol.StringLength) => value.Length > maxLength ? value.Substring(0, maxLength) + Truncated : value;

        public static bool IsBlank(string? value)
        {
            if (value == null) return true;
            foreach (var character in value)
            {
                if (!char.IsWhiteSpace(character)) return false;
            }
            return true;
        }
    }
}
