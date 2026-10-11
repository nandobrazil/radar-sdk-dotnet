using System;
using System.Diagnostics;
using Oconde.Radar.Json;

namespace Oconde.Radar.Runtime
{
    internal static class RuntimeInfo
    {
        private static readonly DateTime Loaded = DateTime.UtcNow;
        private static string? version;

        public static JsonObject Describe()
        {
            var info = new JsonObject();
            info.Set("name", "dotnet");
            info.Set("version", version ?? (version = Version()));
            Try(() => info.Set("host", System.Environment.MachineName));
            Try(() =>
            {
                using (var process = Process.GetCurrentProcess()) info.Set("pid", (long)process.Id);
            });
            Try(() => info.Set("memoryMb", (long)Math.Round(System.Environment.WorkingSet / 1048576.0)));
            info.Set("uptimeS", Uptime());
            return info;
        }

        private static long Uptime()
        {
            try
            {
                using (var process = Process.GetCurrentProcess()) return (long)Math.Round((DateTime.Now - process.StartTime).TotalSeconds);
            }
            catch (Exception)
            {
                return (long)Math.Round((DateTime.UtcNow - Loaded).TotalSeconds);
            }
        }

        private static void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
            }
        }

        private static string Version()
        {
            try
            {
#if NETSTANDARD
                return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Trim();
#else
                if (Type.GetType("Mono.Runtime") != null) return "Mono " + System.Environment.Version;
                return ".NET Framework " + FrameworkVersion();
#endif
            }
            catch (Exception)
            {
                return "CLR " + System.Environment.Version;
            }
        }

#if NETFRAMEWORK
        private static string FrameworkVersion()
        {
            if (System.Environment.Version.Major < 4) return "3.5";
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key?.GetValue("Release") is int release)
                    {
                        if (release >= 533320) return "4.8.1";
                        if (release >= 528040) return "4.8";
                        if (release >= 461808) return "4.7.2";
                        if (release >= 461308) return "4.7.1";
                        if (release >= 460798) return "4.7";
                        if (release >= 394802) return "4.6.2";
                        if (release >= 394254) return "4.6.1";
                        if (release >= 393295) return "4.6";
                        if (release >= 379893) return "4.5.2";
                        if (release >= 378675) return "4.5.1";
                        return "4.5";
                    }
                }
            }
            catch (Exception)
            {
            }
            return "4 (CLR " + System.Environment.Version + ")";
        }
#endif
    }
}
