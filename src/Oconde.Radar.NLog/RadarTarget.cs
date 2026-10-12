using System;
using NLog;
using NLog.Targets;
using Oconde.Radar.Integration;

namespace Oconde.Radar.NLog
{
    [Target("Radar")]
    public sealed class RadarTarget : TargetWithContext
    {
        private readonly RadarClient? client;

        public RadarTarget()
            : this(null)
        {
        }

        public RadarTarget(RadarClient? client)
        {
            this.client = client;
            Layout = "${message}";
            IncludeEventProperties = true;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            if (logEvent == null) return;
            try
            {
                var target = client ?? global::Oconde.Radar.Radar.Client;
                var level = ToRadarLevel(logEvent.Level);
                if (!target.IsLevelEnabled(level)) return;
                var entry = new RadarLogEntry(level, RenderLogEvent(Layout, logEvent))
                {
                    Logger = logEvent.LoggerName,
                    Exception = logEvent.Exception,
                };
                foreach (var property in GetAllProperties(logEvent)) entry.Properties[property.Key] = property.Value;
                target.Write(entry);
            }
            catch (Exception)
            {
            }
        }

        internal static RadarLevel ToRadarLevel(LogLevel? level)
        {
            if (level == null || level >= LogLevel.Error) return RadarLevel.Error;
            if (level >= LogLevel.Warn) return RadarLevel.Warn;
            if (level >= LogLevel.Info) return RadarLevel.Info;
            return RadarLevel.Debug;
        }
    }
}
