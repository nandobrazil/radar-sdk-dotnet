using System;
using NLog;
using NLog.Config;
using Oconde.Radar.NLog;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Logging.Tests
{
    public class NLogTests
    {
        private static Logger LoggerFor(RadarClient client, string name)
        {
            var factory = new LogFactory();
            var configuration = new LoggingConfiguration(factory);
            configuration.AddRule(global::NLog.LogLevel.Trace, global::NLog.LogLevel.Fatal, new RadarTarget(client) { Name = "radar" });
            factory.Configuration = configuration;
            return factory.GetLogger(name);
        }

        [Fact]
        public void SendsTheRenderedMessageWithPropertiesAndLoggerName()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = LoggerFor(client, "Orders.Checkout");
                logger.Warn("Pedido {OrderId} atrasado", 42);
                logger.Debug("detalhe que não sobe");
                var log = Assert.Single(client.Sent(server, "log"));
                Assert.Equal("warn", log.Text("level"));
                Assert.Equal("Pedido 42 atrasado", log.Text("message"));
                Assert.Equal(42, log.Attrs().GetProperty("OrderId").GetInt32());
                Assert.Equal("Orders.Checkout", log.Attrs().Text("logger"));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void TurnsTheExceptionIntoAnOccurrenceOnlyOnceAndIgnoresItsOwnLogs()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = LoggerFor(client, "Orders");
                var error = Ingest.Caught("quebrou");
                logger.Error(error, "Falha no pedido {OrderId}", 9);
                logger.Fatal(error, "de novo");
                LoggerFor(client, "Oconde.Radar.Transport").Error("interno");
                var occurrence = Assert.Single(client.Sent(server, "error"));
                Assert.Equal("Falha no pedido 9", occurrence.Attrs().Text("logMessage"));
                Assert.Empty(client.Sent(server, "log"));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void IsConfigurableFromXml()
        {
            var configuration = XmlLoggingConfiguration.CreateFromXmlString(@"<nlog xmlns=""http://www.nlog-project.org/schemas/NLog.xsd"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" throwConfigExceptions=""true"">
  <extensions><add assembly=""Oconde.Radar.NLog"" /></extensions>
  <targets><target xsi:type=""Radar"" name=""radar"" /></targets>
  <rules><logger name=""*"" minlevel=""Info"" writeTo=""radar"" /></rules>
</nlog>");
            Assert.IsType<RadarTarget>(configuration.FindTargetByName("radar"));
        }
    }
}
