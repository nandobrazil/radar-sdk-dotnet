using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Oconde.Radar;
using Oconde.Radar.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RadarServiceCollectionExtensions
    {
        public static IServiceCollection AddRadar(this IServiceCollection services, IConfiguration? configuration = null, Action<RadarOptions>? configure = null)
        {
            var options = new RadarOptions();
            configuration?.GetSection("Radar").Bind(options);
            configure?.Invoke(options);
            Radar.Client.Init(options);
            return services.AddRadar(Radar.Client);
        }

        public static IServiceCollection AddRadar(this IServiceCollection services, RadarClient client)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(RadarClient))) services.AddSingleton(client);
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(IStartupFilter) && descriptor.ImplementationType == typeof(RadarStartupFilter))) services.AddTransient<IStartupFilter, RadarStartupFilter>();
            return services;
        }
    }
}
