using System;
using System.Linq;
using System.Xml;
using log4net;
using log4net.Config;
using Oconde.Radar.Log4Net;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Logging.Tests
{
    public class Log4NetTests
    {
        private static ILog LoggerFor(RadarClient client, string name)
        {
            var repository = LogManager.CreateRepository(Guid.NewGuid().ToString());
            var appender = new RadarAppender(client);
            appender.ActivateOptions();
            BasicConfigurator.Configure(repository, appender);
            return LogManager.GetLogger(repository.Name, name);
        }

        [Fact]
        public void SendsTheMessageWithContextPropertiesAndLoggerName()
        {
            using (var server = new FakeIngest())
            {
                var client = Ingest.ClientFor(server);
                var logger = LoggerFor(client, "Orders.Checkout");
                ThreadContext.Properties["tenant"] = "acme";
                try
                {
                    logger.Warn("Pedido 42 atrasado");
                    logger.Debug("detalhe que não sobe");
                }
                finally
                {
                    ThreadContext.Properties.Remove("tenant");
                }
                var log = Assert.Single(client.Sent(server, "log"));
                Assert.Equal("warn", log.Text("level"));
                Assert.Equal("Pedido 42 atrasado", log.Text("message"));
                Assert.Equal("acme", log.Attrs().Text("tenant"));
                Assert.Equal("Orders.Checkout", log.Attrs().Text("logger"));
                Assert.DoesNotContain(log.Attrs().EnumerateObject(), property => property.Name.StartsWith("log4net:", StringComparison.Ordinal));
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
                logger.Error("Falha no pedido 9", error);
                logger.Fatal("de novo", error);
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
            var document = new XmlDocument();
            document.LoadXml(@"<log4net>
  <appender name=""radar"" type=""Oconde.Radar.Log4Net.RadarAppender, Oconde.Radar.Log4Net"" />
  <root><level value=""INFO"" /><appender-ref ref=""radar"" /></root>
</log4net>");
            var repository = LogManager.CreateRepository(Guid.NewGuid().ToString());
            XmlConfigurator.Configure(repository, document.DocumentElement!);
            Assert.Contains(repository.GetAppenders(), appender => appender is RadarAppender);
        }
    }
}
