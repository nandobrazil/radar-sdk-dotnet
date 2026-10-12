using System;
using System.Collections.Generic;
using System.Linq;
using Oconde.Radar.Integration;
using Serilog.Core;
using Serilog.Events;

namespace Oconde.Radar.Serilog
{
    public sealed class RadarSink : ILogEventSink
    {
        private const string SourceContext = "SourceContext";
        private readonly RadarClient client;
        private readonly IFormatProvider? formatProvider;

        public RadarSink(RadarClient? client = null, IFormatProvider? formatProvider = null)
        {
            this.client = client ?? global::Oconde.Radar.Radar.Client;
            this.formatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) return;
            try
            {
                var level = ToRadarLevel(logEvent.Level);
                if (!client.IsLevelEnabled(level)) return;
                var entry = new RadarLogEntry(level, logEvent.RenderMessage(formatProvider)) { Exception = logEvent.Exception };
                foreach (var property in logEvent.Properties)
                {
                    if (property.Key == SourceContext)
                    {
                        entry.Logger = (property.Value as ScalarValue)?.Value as string;
                        continue;
                    }
                    entry.Properties[property.Key] = Plain(property.Value);
                }
                client.Write(entry);
            }
            catch (Exception)
            {
            }
        }

        internal static RadarLevel ToRadarLevel(LogEventLevel level)
        {
            switch (level)
            {
                case LogEventLevel.Verbose:
                case LogEventLevel.Debug:
                    return RadarLevel.Debug;
                case LogEventLevel.Information:
                    return RadarLevel.Info;
                case LogEventLevel.Warning:
                    return RadarLevel.Warn;
                default:
                    return RadarLevel.Error;
            }
        }

        private static object? Plain(LogEventPropertyValue value)
        {
            switch (value)
            {
                case ScalarValue scalar:
                    return scalar.Value;
                case SequenceValue sequence:
                    return sequence.Elements.Select(Plain).ToList();
                case StructureValue structure:
                    return structure.Properties.GroupBy(property => property.Name).ToDictionary(group => group.Key, group => Plain(group.Last().Value), StringComparer.Ordinal);
                case DictionaryValue dictionary:
                    return dictionary.Elements.GroupBy(element => Convert.ToString(element.Key.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToDictionary(group => group.Key, group => Plain(group.Last().Value), StringComparer.Ordinal);
                default:
                    return value?.ToString();
            }
        }
    }
}
