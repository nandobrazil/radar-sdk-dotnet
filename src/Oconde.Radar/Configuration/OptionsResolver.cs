using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Oconde.Radar.Configuration
{
    internal static class OptionsResolver
    {
        public static ResolvedOptions Resolve(RadarOptions? options)
        {
            options = options ?? new RadarOptions();
            var key = Clean(options.Key) ?? Setting("RADAR_KEY", "Radar:Key");
            var requests = options.LogRequests ?? new RadarLogRequests();
            return new ResolvedOptions
            {
                Key = key,
                Endpoint = (Clean(options.Endpoint) ?? Setting("RADAR_ENDPOINT", "Radar:Endpoint") ?? ResolvedOptions.DefaultEndpoint).TrimEnd('/'),
                Environment = Clean(options.Environment) ?? Setting("RADAR_ENVIRONMENT", "Radar:Environment") ?? "production",
                Release = Clean(options.Release) ?? Setting("RADAR_RELEASE", "Radar:Release"),
                Enabled = options.Enabled ?? key != null,
                Console = options.Console,
                MinLevel = options.MinLevel,
                Redact = options.Redact,
                RequestDetail = options.RequestDetail,
                LogRequests = requests.Enabled,
                Include = Patterns(requests.Include),
                Exclude = Patterns(requests.Exclude),
                IncludeMatching = (requests.IncludeMatching ?? new List<Regex>()).Where(pattern => pattern != null).ToArray(),
                ExcludeMatching = (requests.ExcludeMatching ?? new List<Regex>()).Where(pattern => pattern != null).ToArray(),
                Sample = double.IsNaN(requests.Sample) || requests.Sample < 0 || requests.Sample > 1 ? 1.0 : requests.Sample,
                IgnorePaths = new HashSet<string>((options.IgnorePaths ?? new List<string>()).Where(path => !string.IsNullOrEmpty(path))),
                CaptureUnhandled = options.CaptureUnhandled,
                Debug = options.Debug,
                EnableTls12 = options.EnableTls12,
            };
        }

        public static ResolvedOptions Inactive() => new ResolvedOptions { Enabled = false, CaptureUnhandled = false };

        private static string[] Patterns(IList<string>? values) => (values ?? new List<string>()).Select(Clean).Where(value => value != null).Select(value => value!).ToArray();

        private static string? Clean(string? value) => Text.IsBlank(value) ? null : value!.Trim();

        private static string? Setting(string variable, string appSetting)
        {
            try
            {
                var value = Clean(System.Environment.GetEnvironmentVariable(variable));
                if (value != null) return value;
            }
            catch (Exception)
            {
            }
#if NETFRAMEWORK
            try
            {
                return Clean(System.Configuration.ConfigurationManager.AppSettings[appSetting]);
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }
    }
}
