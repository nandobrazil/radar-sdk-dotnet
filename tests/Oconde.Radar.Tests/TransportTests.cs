using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Tests
{
    public class TransportTests
    {
        [Fact]
        public void DeliversEveryEventFromManyThreadsOnceAndGzipsLargeBatches()
        {
            using (var server = new FakeIngest())
            {
                var client = TestClients.For(server);
                var threads = Enumerable.Range(0, 8).Select(thread => new Thread(() =>
                {
                    for (var index = 0; index < 25; index++) client.Info("evento", new { thread, index, texto = new string('z', 200) });
                })).ToList();
                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());
                Assert.True(client.Flush(TimeSpan.FromSeconds(10)));
                var keys = server.Logs().Select(item => item.Attrs().GetProperty("thread").GetInt32() + ":" + item.Attrs().GetProperty("index").GetInt32()).ToList();
                Assert.Equal(200, keys.Count);
                Assert.Equal(200, keys.Distinct().Count());
                Assert.Contains(server.Requests, request => request.Compressed);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void TriesAgainAfterAServerErrorAndHonorsRetryAfter()
        {
            using (var server = new FakeIngest())
            {
                server.Enqueue(500);
                server.Enqueue(429, new Dictionary<string, string> { ["Retry-After"] = "1" });
                var client = TestClients.For(server);
                client.Info("insistente");
                var watch = Stopwatch.StartNew();
                Assert.True(server.WaitFor(3, TimeSpan.FromSeconds(10)));
                Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(0.9));
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                Assert.Single(server.Logs().Where(item => item.Text("message") == "insistente").Take(1));
                Assert.Equal(3, server.Requests.Count);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void StopsSendingForAWhileWhenTheKeyIsRefused()
        {
            using (var server = new FakeIngest())
            {
                server.Enqueue(401);
                var client = TestClients.For(server);
                client.Info("primeiro");
                Assert.True(server.WaitFor(1, TimeSpan.FromSeconds(5)));
                Thread.Sleep(300);
                client.Info("segundo");
                client.Flush(TimeSpan.FromSeconds(1));
                Thread.Sleep(500);
                Assert.Single(server.Requests);
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public void CountsWhatTheFullQueueDropsAndReportsIt()
        {
            using (var server = new FakeIngest())
            {
                server.Delay = TimeSpan.FromMilliseconds(600);
                var client = TestClients.For(server, settings: TestClients.Fast(maxQueue: 5));
                for (var index = 0; index < 20; index++) client.Info("item " + index);
                Assert.True(client.Flush(TimeSpan.FromSeconds(10)));
                client.Info("depois");
                Assert.True(client.Flush(TimeSpan.FromSeconds(10)));
                Assert.Contains(server.Batches(), batch => batch.TryGetProperty("dropped", out var dropped) && dropped.GetInt32() > 0);
                client.Close(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void FlushReturnsBeforeItsDeadlineWhenTheServerIsSlow()
        {
            using (var server = new FakeIngest())
            {
                server.Delay = TimeSpan.FromSeconds(3);
                var client = TestClients.For(server, settings: new Oconde.Radar.Transport.TransportSettings { FlushInterval = TimeSpan.FromSeconds(30), BatchSize = 100, MaxQueue = 1000, Timeout = TimeSpan.FromSeconds(5), BackoffBase = TimeSpan.FromMilliseconds(100) });
                client.Info("lento");
                var watch = Stopwatch.StartNew();
                client.Flush(TimeSpan.FromMilliseconds(500));
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), watch.Elapsed.ToString());
                client.Close(TimeSpan.Zero);
            }
        }
    
        [Fact]
        public void WaitsOutTheRetryWithoutSpinning()
        {
            using (var server = new FakeIngest())
            {
                server.Enqueue(503, new Dictionary<string, string> { ["Retry-After"] = "30" });
                var client = TestClients.For(server);
                for (var index = 0; index < 150; index++) client.Info("fila", new { index });
                Assert.True(server.WaitFor(1, TimeSpan.FromSeconds(5)));
                Thread.Sleep(300);
                var process = Process.GetCurrentProcess();
                var before = process.TotalProcessorTime;
                Thread.Sleep(1000);
                process.Refresh();
                var used = process.TotalProcessorTime - before;
                Assert.True(used < TimeSpan.FromMilliseconds(400), "CPU em 1 s: " + used.TotalMilliseconds + " ms");
                Assert.Single(server.Requests);
                client.Close(TimeSpan.Zero);
            }
        }
}
}
