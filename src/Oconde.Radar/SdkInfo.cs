using System;
using System.Linq;
using System.Reflection;

namespace Oconde.Radar
{
    internal static class SdkInfo
    {
        public const string Name = "Oconde.Radar";

        public static readonly string Version = ReadVersion();

        private static string ReadVersion()
        {
            try
            {
                var attribute = typeof(SdkInfo).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                var version = attribute?.InformationalVersion ?? typeof(SdkInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }
    }
}
