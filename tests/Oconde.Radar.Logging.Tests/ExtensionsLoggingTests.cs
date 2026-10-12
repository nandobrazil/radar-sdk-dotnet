using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Logging.Tests
{
    public class ExtensionsLoggingTests
    {
        private static ILoggerFactory FactoryFor(RadarClient client) => new ServiceCollection()
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddRadar(client))
            .BuildServiceProvider()
            .GetRequiredService<ILoggerFactory>();

        [Fact]
        public void SendsTheRenderedMessageWithPropertiesScopesAndCategory()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = FactoryFor(client).CreateLogger("Orders.Checkout");
                using (logger.BeginScope(new Dictionary<string, object> { ["tenant"] = "acme", ["RequestPath"] = "/reset/segredo" }))
                using (logger.BeginScope("lote {Batch}", 7))
                {
                    logger.LogWarning("Pedido {OrderId} atrasado", 42);
                    logger.LogDebug("detalhe que não sobe");
                }
                var log = Assert.Single(client.Sent(server, "log"));
                Assert.Equal("warn", log.Text("level"));
                Assert.Equal("Pedido 42 atrasado", log.Text("message"));
                var attrs = log.Attrs();
                Assert.Equal(42, attrs.GetProperty("OrderId").GetInt32());
                Assert.Equal("acme", attrs.Text("tenant"));
                Assert.Equal(7, attrs.GetProperty("Batch").GetInt32());
                Assert.Equal("Orders.Checkout", attrs.Text("logger"));
                Assert.False(attrs.TryGetProperty("{OriginalFormat}", out _));
                Assert.False(attrs.TryGetProperty("RequestPath", out _));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void TurnsTheExceptionIntoAnOccurrenceOnlyOnce()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = FactoryFor(client).CreateLogger("Orders");
                var error = Ingest.Caught("quebrou");
                logger.LogError(error, "Falha no pedido {OrderId}", 9);
                logger.LogCritical(error, "de novo");
                var already = Ingest.Caught("já capturada");
                client.CaptureException(already);
                logger.LogError(already, "repetida");
                var errors = client.Sent(server, "error");
                Assert.Equal(2, errors.Count);
                var first = errors[0];
                Assert.True(first.GetProperty("handled").GetBoolean());
                Assert.Equal("quebrou", first.GetProperty("exception").Text("message"));
                Assert.Equal("Falha no pedido 9", first.Attrs().Text("logMessage"));
                Assert.Equal(9, first.Attrs().GetProperty("OrderId").GetInt32());
                Assert.Equal("Orders", first.Attrs().Text("logger"));
                Assert.Empty(client.Sent(server, "log"));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void TakesFrameworkCategoriesOnlyFromWarningAndIgnoresItsOwn()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var factory = FactoryFor(client);
                factory.CreateLogger("Microsoft.AspNetCore.Routing").LogInformation("rota");
                factory.CreateLogger("System.Net.Http.HttpClient").LogInformation("chamada");
                factory.CreateLogger("Microsoft.AspNetCore.Routing").LogWarning("aviso");
                factory.CreateLogger("Oconde.Radar.Transport").LogError("interno");
                var log = Assert.Single(client.Sent(server, "log"));
                Assert.Equal("aviso", log.Text("message"));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void MarksFrameworkReportsOfUnhandledExceptionsAsUnhandled()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                FactoryFor(client).CreateLogger("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware").LogError(Ingest.Caught("escapou"), "An unhandled exception has occurred while executing the request.");
                var error = Assert.Single(client.Sent(server, "error"));
                Assert.False(error.GetProperty("handled").GetBoolean());
                client.Close(TimeSpan.Zero);
            }
        }
    }
}
