using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oconde.Radar;
using Oconde.Radar.Extensions.Logging;

namespace Microsoft.Extensions.Logging
{
    public static class RadarLoggingBuilderExtensions
    {
        public static ILoggingBuilder AddRadar(this ILoggingBuilder builder) => builder.AddRadar(null);

        public static ILoggingBuilder AddRadar(this ILoggingBuilder builder, RadarClient? client)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, RadarLoggerProvider>(services => new RadarLoggerProvider(client ?? services.GetService<RadarClient>())));
            builder.AddFilter<RadarLoggerProvider>("Microsoft", LogLevel.Warning);
            builder.AddFilter<RadarLoggerProvider>("System", LogLevel.Warning);
            return builder;
        }
    }
}
