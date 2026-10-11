using System;
using System.Globalization;

namespace Oconde.Radar.Transport
{
    internal sealed class HttpOutcome
    {
        public const double MaxRetryAfterSeconds = 86400;

        public HttpOutcome(int status, double? retryAfterSeconds, string? error)
        {
            Status = status;
            RetryAfterSeconds = retryAfterSeconds;
            Error = error;
        }

        public int Status { get; }
        public double? RetryAfterSeconds { get; }
        public string? Error { get; }

        public static double? ParseRetryAfter(string? header)
        {
            if (Text.IsBlank(header)) return null;
            var value = header!.Trim();
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return Clamp(seconds);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)) return Clamp((when - DateTime.UtcNow).TotalSeconds);
            return null;
        }

        private static double? Clamp(double seconds) => double.IsNaN(seconds) || double.IsInfinity(seconds) ? (double?)null : Math.Min(Math.Max(0, seconds), MaxRetryAfterSeconds);
    }
}
