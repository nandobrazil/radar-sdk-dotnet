using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Oconde.Radar.Testing;
using Oconde.Radar.Transport;

namespace Oconde.Radar.Logging.Tests
{
    internal static class Ingest
    {
        public static RadarClient ClientFor(FakeIngest server)
        {
            var client = new RadarClient();
            client.Init(new RadarOptions { Key = "rk_test", Endpoint = server.Url, CaptureUnhandled = false }, new TransportSettings { FlushInterval = TimeSpan.FromMilliseconds(100), BackoffBase = TimeSpan.FromMilliseconds(100) });
            return client;
        }

        public static List<JsonElement> Sent(this RadarClient client, FakeIngest server, string type)
        {
            if (!client.Flush(TimeSpan.FromSeconds(5))) throw new TimeoutException("flush");
            return server.Events().Where(item => item.GetProperty("type").GetString() == type).ToList();
        }

        public static string? Text(this JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        public static JsonElement Attrs(this JsonElement element) => element.GetProperty("attrs");

        public static Exception Caught(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception error)
            {
                return error;
            }
        }
    }
}
