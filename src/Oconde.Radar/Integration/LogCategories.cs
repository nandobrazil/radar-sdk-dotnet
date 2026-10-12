using System;

namespace Oconde.Radar.Integration
{
    internal static class LogCategories
    {
        private static readonly string[] UnhandledReporters = { "Microsoft.AspNetCore.Diagnostics.", "Microsoft.AspNetCore.Server.", "Microsoft.Extensions.Hosting.Internal.Host" };
        private static readonly string[] Libraries = { "Microsoft.", "System." };

        public static bool ReportsUnhandled(string? logger) => StartsWithAny(logger, UnhandledReporters);

        public static bool IsLibrary(string? logger) => StartsWithAny(logger, Libraries);

        private static bool StartsWithAny(string? logger, string[] prefixes)
        {
            if (logger == null) return false;
            foreach (var prefix in prefixes)
            {
                if (logger.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
