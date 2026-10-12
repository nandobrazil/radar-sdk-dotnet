using System;
using System.Text.RegularExpressions;
using Oconde.Radar.Configuration;

namespace Oconde.Radar.Integration
{
    internal static class RequestFilters
    {
        [ThreadStatic]
        private static Random? random;

        public static bool ShouldLog(ResolvedOptions options, string path, string? route)
        {
            if (!options.LogRequests || options.IgnorePaths.Contains(path)) return false;
            if ((options.Include.Length > 0 || options.IncludeMatching.Length > 0) && !Matches(options.Include, options.IncludeMatching, path, route)) return false;
            if (Matches(options.Exclude, options.ExcludeMatching, path, route)) return false;
            if (options.Sample < 1 && (random ?? (random = new Random(Guid.NewGuid().GetHashCode()))).NextDouble() >= options.Sample) return false;
            return true;
        }

        private static bool Matches(string[] prefixes, Regex[] patterns, string path, string? route)
        {
            foreach (var prefix in prefixes)
            {
                if (MatchesPrefix(prefix, path) || (route != null && MatchesPrefix(prefix, route))) return true;
            }
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(path) || (route != null && pattern.IsMatch(route))) return true;
            }
            return false;
        }

        private static bool MatchesPrefix(string prefix, string candidate)
        {
            var withSlash = prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
            return candidate == prefix || candidate.StartsWith(withSlash, StringComparison.Ordinal);
        }
    }
}
