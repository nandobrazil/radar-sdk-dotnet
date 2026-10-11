using System;
using System.Reflection;

namespace Oconde.Radar.Stack
{
    internal static class InApp
    {
        private static readonly string[] Libraries =
        {
            "System", "Microsoft", "mscorlib", "netstandard", "Windows", "WindowsBase", "PresentationCore", "PresentationFramework", "Newtonsoft.Json", "Serilog", "NLog", "log4net", "xunit",
            "Castle.", "EntityFramework", "Npgsql", "MySql.", "MySqlConnector", "Oracle.", "Dapper", "AutoMapper", "MediatR", "Polly", "FluentValidation", "Swashbuckle", "StackExchange.", "RabbitMQ.",
            "Hangfire", "Quartz", "Grpc", "Google.", "NHibernate", "Autofac", "Ninject", "SimpleInjector", "Owin", "Nancy", "Oconde.Radar",
        };

        public static bool Contains(MethodBase method)
        {
            try
            {
                var assembly = method.DeclaringType?.Assembly ?? method.Module.Assembly;
                if (assembly.GlobalAssemblyCache) return false;
                return !IsLibrary(assembly.GetName().Name ?? string.Empty);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsLibrary(string name)
        {
            if (name.EndsWith(".Tests", StringComparison.Ordinal)) return false;
            foreach (var prefix in Libraries)
            {
                if (prefix.EndsWith(".", StringComparison.Ordinal) ? name.StartsWith(prefix, StringComparison.Ordinal) : name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
