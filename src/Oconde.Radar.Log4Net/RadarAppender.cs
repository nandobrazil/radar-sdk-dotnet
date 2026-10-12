using System;
using log4net.Appender;
using log4net.Core;
using Oconde.Radar.Integration;

namespace Oconde.Radar.Log4Net
{
    public sealed class RadarAppender : AppenderSkeleton
    {
        private const string ReservedPrefix = "log4net:";
        private readonly RadarClient? client;

        public RadarAppender()
            : this(null)
        {
        }

        public RadarAppender(RadarClient? client)
        {
            this.client = client;
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (loggingEvent == null) return;
            try
            {
                var target = client ?? global::Oconde.Radar.Radar.Client;
                var level = ToRadarLevel(loggingEvent.Level);
                if (!target.IsLevelEnabled(level)) return;
                var entry = new RadarLogEntry(level, loggingEvent.RenderedMessage)
                {
                    Logger = loggingEvent.LoggerName,
                    Exception = loggingEvent.ExceptionObject,
                };
                AddProperties(loggingEvent, entry);
                target.Write(entry);
            }
            catch (Exception)
            {
            }
        }

        private static void AddProperties(LoggingEvent loggingEvent, RadarLogEntry entry)
        {
            try
            {
                var properties = loggingEvent.GetProperties();
                foreach (var key in properties.GetKeys())
                {
                    if (key == null || key.StartsWith(ReservedPrefix, StringComparison.Ordinal)) continue;
                    entry.Properties[key] = properties[key];
                }
            }
            catch (Exception)
            {
            }
        }

        internal static RadarLevel ToRadarLevel(Level? level)
        {
            if (level == null || level >= Level.Error) return RadarLevel.Error;
            if (level >= Level.Warn) return RadarLevel.Warn;
            if (level >= Level.Info) return RadarLevel.Info;
            return RadarLevel.Debug;
        }
    }
}
