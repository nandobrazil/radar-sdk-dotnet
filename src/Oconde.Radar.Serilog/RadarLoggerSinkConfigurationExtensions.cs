using System;
using Oconde.Radar;
using Oconde.Radar.Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace Serilog
{
    public static class RadarLoggerSinkConfigurationExtensions
    {
        public static LoggerConfiguration Radar(this LoggerSinkConfiguration sinkConfiguration, LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose, RadarClient? client = null, IFormatProvider? formatProvider = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            return sinkConfiguration.Sink(new RadarSink(client, formatProvider), restrictedToMinimumLevel);
        }
    }
}
