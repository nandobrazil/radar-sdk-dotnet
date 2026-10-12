using System;
using Oconde.Radar.Testing;
using Serilog;
using Xunit;

namespace Oconde.Radar.Logging.Tests
{
    public class SerilogTests
    {
        private static ILogger LoggerFor(RadarClient client) => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Radar(client: client).CreateLogger();

        [Fact]
        public void SendsTheRenderedMessageWithPropertiesAndSourceContext()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = LoggerFor(client).ForContext("SourceContext", "Orders.Checkout");
                logger.Information("Pedido {OrderId} com {@Item}", 42, new { Sku = "X1", Qty = 2 });
                logger.Debug("detalhe que não sobe");
                var log = Assert.Single(client.Sent(server, "log"));
                Assert.Equal("info", log.Text("level"));
                Assert.StartsWith("Pedido 42 com", log.Text("message"));
                var attrs = log.Attrs();
                Assert.Equal(42, attrs.GetProperty("OrderId").GetInt32());
                Assert.Equal("X1", attrs.GetProperty("Item").Text("Sku"));
                Assert.Equal("Orders.Checkout", attrs.Text("logger"));
                Assert.False(attrs.TryGetProperty("SourceContext", out _));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void TurnsTheExceptionIntoAnOccurrenceOnlyOnceAndIgnoresItsOwnLogs()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = LoggerFor(client);
                var error = Ingest.Caught("quebrou");
                logger.Error(error, "Falha no pedido {OrderId}", 9);
                logger.Fatal(error, "de novo");
                logger.ForContext("SourceContext", "Oconde.Radar.Transport").Warning("interno");
                var occurrence = Assert.Single(client.Sent(server, "error"));
                Assert.Equal("Falha no pedido 9", occurrence.Attrs().Text("logMessage"));
                Assert.Equal(9, occurrence.Attrs().GetProperty("OrderId").GetInt32());
                Assert.Empty(client.Sent(server, "log"));
                client.Close(TimeSpan.Zero);
            }
        }
    }
}
