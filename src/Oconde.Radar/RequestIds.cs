using System;

namespace Oconde.Radar
{
    internal static class RequestIds
    {
        public static string From(string? incoming)
        {
            var value = incoming?.Trim();
            if (!string.IsNullOrEmpty(value) && value!.Length <= Protocol.RequestIdLength && IsSafe(value)) return value;
            return Guid.NewGuid().ToString();
        }

        private static bool IsSafe(string value)
        {
            foreach (var character in value)
            {
                if (character < 0x21 || character > 0x7e) return false;
            }
            return true;
        }
    }
}
