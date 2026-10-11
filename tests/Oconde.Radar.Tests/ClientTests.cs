using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Tests
{
    [Collection("environment")]
    public class EnvironmentTests
    {
        [Fact]
        public void StaysOffWithoutAKey()
        {
            Environment.SetEnvironmentVariable("RADAR_KEY", null);
            var client = new RadarClient();
            client.Init();
            Assert.False(client.IsEnabled);
            client.Info("nada");
            Assert.True(client.Flush(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void ReadsTheKeyAndTheSettingsFromTheEnvironment()
        {
            using (var server = new FakeIngest())
            {
                Environment.SetEnvironmentVariable("RADAR_KEY", "rk_env");
                Environment.SetEnvironmentVariable("RADAR_ENDPOINT", server.Url + "/");
                Environment.SetEnvironmentVariable("RADAR_ENVIRONMENT", "homolog");
                Environment.SetEnvironmentVariable("RADAR_RELEASE", "abc123");
                try
                {
                    var client = new RadarClient();
                    client.Init(new RadarOptions { CaptureUnhandled = false }, TestClients.Fast());
                    client.Info("do ambiente");
                    Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                    var batch = server.Batches()[0];
                    Assert.Equal(1, batch.GetProperty("v").GetInt32());
                    Assert.Equal("homolog", batch.Text("environment"));
                    Assert.Equal("abc123", batch.Text("release"));
                    Assert.Equal("Oconde.Radar", batch.GetProperty("sdk").Text("name"));
                    Assert.Equal("Bearer rk_env", server.Requests[0].Headers["authorization"]);
                    client.Close(TimeSpan.FromSeconds(1));
                }
                finally
                {
                    foreach (var name in new[] { "RADAR_KEY", "RADAR_ENDPOINT", "RADAR_ENVIRONMENT", "RADAR_RELEASE" }) Environment.SetEnvironmentVariable(name, null);
                }
            }
        }

        [Fact]
        public void WritesJsonLinesToTheConsoleWhenAsked()
        {
            Environment.SetEnvironmentVariable("RADAR_KEY", null);
            var output = new StringWriter();
            var error = new StringWriter();
            var previousOut = Console.Out;
            var previousError = Console.Error;
            Console.SetOut(output);
            Console.SetError(error);
            try
            {
                var client = new RadarClient();
                client.Init(new RadarOptions { Console = true, CaptureUnhandled = false });
                client.Info("no console", new { a = 1 });
                client.CaptureException(SampleErrors.Caught(SampleErrors.LoadOrder));
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }
            var line = JsonDocument.Parse(output.ToString().Trim()).RootElement;
            Assert.Equal("info", line.Text("level"));
            Assert.Equal("no console", line.Text("message"));
            Assert.Equal(1, line.GetProperty("a").GetInt32());
            Assert.StartsWith("System.Collections.Generic.KeyNotFoundException:", JsonDocument.Parse(error.ToString().Trim().Split('\n').Last()).RootElement.Text("message"));
        }
    }

    public class ClientTests
    {
        [Fact]
        public void LogsWithMaskedAttributesAndTheMinimumLevel()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                client.Debug("fica de fora");
                client.Info("pedido criado", new { pedido = 42, senha = "hunter2", cliente = "Ana" });
                client.Warn(new string('x', 3000));
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var logs = server.Logs();
                Assert.Equal(new[] { "info", "warn" }, logs.Select(item => item.Text("level")).ToArray());
                Assert.Equal("pedido criado", logs[0].Text("message"));
                Assert.Equal(42, logs[0].Attrs().GetProperty("pedido").GetInt32());
                Assert.StartsWith("••• #", logs[0].Attrs().Text("senha"));
                Assert.EndsWith("…[cortado]", logs[1].Text("message"));
                Assert.False(logs[0].TryGetProperty("requestId", out _));
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void CapturesAnExceptionOnceWithTheRuntime()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                var error = SampleErrors.Caught(SampleErrors.LoadOrder);
                client.CaptureException(error, new { pedido = 7 });
                client.CaptureException(error);
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var captured = Assert.Single(server.Errors());
                Assert.Equal("error", captured.Text("level"));
                Assert.True(captured.GetProperty("handled").GetBoolean());
                Assert.Equal("System.Collections.Generic.KeyNotFoundException", captured.GetProperty("exception").Text("type"));
                Assert.Equal(7, captured.Attrs().GetProperty("pedido").GetInt32());
                var runtime = captured.GetProperty("runtime");
                Assert.Equal("dotnet", runtime.Text("name"));
                Assert.False(string.IsNullOrEmpty(runtime.Text("version")));
                Assert.True(runtime.GetProperty("pid").GetInt32() > 0);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void GroupsTheWorkOfAScopeUnderARequestIdAcrossThreadsAndTasks()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                using (client.StartScope("job-77"))
                {
                    client.SetUser("9", "ana@loja.dev");
                    client.Info("começou");
                    var thread = new Thread(() => client.Info("na thread"));
                    thread.Start();
                    thread.Join();
                    ThreadPool.QueueUserWorkItem(_ => client.Info("no pool"));
                    Thread.Sleep(300);
                }
                client.Info("fora");
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var logs = server.Logs();
                Assert.All(logs.Where(item => item.Text("message") != "fora"), item => Assert.Equal("job-77", item.Text("requestId")));
                Assert.All(logs.Where(item => item.Text("message") != "fora"), item => Assert.Equal("ana@loja.dev", item.GetProperty("user").Text("email")));
                var outside = logs.Single(item => item.Text("message") == "fora");
                Assert.False(outside.TryGetProperty("requestId", out _));
                Assert.False(outside.TryGetProperty("user", out _));
                Assert.Equal(4, logs.Count);
                using (var scope = client.StartScope()) Assert.Equal(36, scope.RequestId.Length);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

#if !NET48
        [Fact]
        public async System.Threading.Tasks.Task KeepsTheScopeAcrossAwait()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                using (client.StartScope("async-1"))
                {
                    await System.Threading.Tasks.Task.Delay(20);
                    client.Info("depois do await");
                    await System.Threading.Tasks.Task.Run(() => client.Info("no Task.Run"));
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                Assert.All(server.Logs(), item => Assert.Equal("async-1", item.Text("requestId")));
                Assert.Equal(2, server.Logs().Count);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }
#endif

        [Fact]
        public void TracksWorkAndReportsItsFailure()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                Assert.Equal(3, client.Track("importar", () => 3, new { arquivo = "a.csv" }));
                Assert.Throws<InvalidOperationException>(() => client.Track("exportar", () => throw new InvalidOperationException("disco cheio")));
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var logs = server.Logs();
                var ok = logs.Single(item => item.Text("message") == "importar");
                Assert.True(ok.Attrs().GetProperty("ok").GetBoolean());
                Assert.Equal("a.csv", ok.Attrs().Text("arquivo"));
                Assert.True(ok.Attrs().GetProperty("durationMs").GetDouble() >= 0);
                var failed = logs.Single(item => item.Text("message") == "exportar");
                Assert.Equal("error", failed.Text("level"));
                Assert.False(failed.Attrs().GetProperty("ok").GetBoolean());
                var error = Assert.Single(server.Errors());
                Assert.Equal("disco cheio", error.GetProperty("exception").Text("message"));
                Assert.Equal(failed.Text("requestId"), error.Text("requestId"));
                Assert.NotEqual(ok.Text("requestId"), failed.Text("requestId"));
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void JobsCheckInAtTheStartAndTheEndWithTheDuration()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server, options => options.Debug = true);
                Assert.Equal("feito", client.RunJob("backup-diario", () => "feito"));
                Assert.Throws<InvalidOperationException>(() => client.RunJob("relatorio", () => throw new InvalidOperationException("sem dados")));
                client.CheckIn("Slug Inválido", CheckInStatus.Ok);
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var checkIns = server.CheckIns().Select(request => new { slug = request.Path.Substring(request.Path.LastIndexOf('/') + 1), json = request.Json }).ToList();
                Assert.Equal(new[] { "backup-diario:in_progress", "backup-diario:ok", "relatorio:in_progress", "relatorio:error" }, checkIns.Select(item => item.slug + ":" + item.json.Text("status")).ToArray());
                Assert.Equal(checkIns[0].json.Text("checkInId"), checkIns[1].json.Text("checkInId"));
                Assert.True(checkIns[1].json.GetProperty("durationMs").GetInt64() >= 0);
                Assert.Equal("staging", checkIns[1].json.Text("environment"));
                Assert.Equal("2.0.1", checkIns[1].json.Text("release"));
                var error = Assert.Single(server.Errors());
                Assert.Equal("relatorio", error.Attrs().Text("cron"));
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void NeverThrowsIntoTheApp()
        {
            var client = new RadarClient();
            client.Info("antes do init");
            client.CaptureException(new InvalidOperationException("antes do init"));
            client.CheckIn("x", CheckInStatus.Ok);
            using (client.StartScope()) client.SetUser(null, null, null);
            client.Close(TimeSpan.Zero);
            client.Info("depois de fechar");
        }
    }
}
