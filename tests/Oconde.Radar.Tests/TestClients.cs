using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Oconde.Radar.Testing;
using Oconde.Radar.Transport;

namespace Oconde.Radar.Tests
{
    internal static class TestClients
    {
        public static TransportSettings Fast(int maxQueue = 1000) => new TransportSettings { FlushInterval = TimeSpan.FromMilliseconds(100), BatchSize = 100, MaxQueue = maxQueue, Timeout = TimeSpan.FromSeconds(5), BackoffBase = TimeSpan.FromMilliseconds(100) };

        public static RadarClient For(FakeIngest server, Action<RadarOptions>? configure = null, TransportSettings? settings = null)
        {
            var options = new RadarOptions { Key = "rk_test", Endpoint = server.Url, Environment = "staging", Release = "2.0.1", CaptureUnhandled = false };
            configure?.Invoke(options);
            var client = new RadarClient();
            client.Init(options, settings ?? Fast());
            return client;
        }

        public static List<JsonElement> Logs(this FakeIngest server) => server.Events().Where(item => item.GetProperty("type").GetString() == "log").ToList();

        public static List<JsonElement> Errors(this FakeIngest server) => server.Events().Where(item => item.GetProperty("type").GetString() == "error").ToList();

        public static string? Text(this JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        public static JsonElement Attrs(this JsonElement element) => element.GetProperty("attrs");
    }
}
