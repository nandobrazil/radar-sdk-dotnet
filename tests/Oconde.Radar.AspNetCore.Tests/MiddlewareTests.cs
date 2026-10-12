using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oconde.Radar.AspNetCore;
using Oconde.Radar.Testing;
using Oconde.Radar.Transport;
using Xunit;

namespace Oconde.Radar.AspNetCore.Tests
{
    public class MiddlewareTests
    {
        private static RadarClient ClientFor(FakeIngest server, Action<RadarOptions>? configure = null)
        {
            var options = new RadarOptions { Key = "rk_test", Endpoint = server.Url, Environment = "staging", CaptureUnhandled = false };
            configure?.Invoke(options);
            var client = new RadarClient();
            client.Init(options, new TransportSettings { FlushInterval = TimeSpan.FromMilliseconds(100), BackoffBase = TimeSpan.FromMilliseconds(100) });
            return client;
        }

        private static async Task<WebApplication> Start(RadarClient client, Action<WebApplication>? pipeline = null, bool useRadar = false, bool logToRadar = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            if (logToRadar) builder.Logging.AddRadar(client);
            builder.Services.AddRadar(client);
            var app = builder.Build();
            if (useRadar) app.UseRadar();
            pipeline?.Invoke(app);
            app.MapGet("/orders/{id}", (string id) =>
            {
                client.SetUser("u-7");
                client.Info("dentro", new { pedido = id });
                return Results.Ok(new { ok = true });
            });
            app.MapPost("/boom", (HttpContext context) => { throw new InvalidOperationException("quebrou"); });
            app.MapGet("/reset/{token}", (string token) => Results.Ok());
            app.MapGet("/healthz", () => Results.Ok("ok"));
            await app.StartAsync();
            return app;
        }

        private static List<JsonElement> RequestLogs(FakeIngest server) => server.Events().Where(item => item.GetProperty("type").GetString() == "log" && item.GetProperty("message").GetString() == "http.request").ToList();

        [Fact]
        public async Task OpensAContextPerRequestEchoesTheRequestIdAndLogsTheResponse()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server);
                await using (var app = await Start(client))
                {
                    var http = app.GetTestClient();
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders/42?token=abcdefghijklmnop&page=2");
                    request.Headers.Add("x-request-id", "req-1");
                    var response = await http.SendAsync(request);
                    Assert.Equal("req-1", response.Headers.GetValues("x-request-id").Single());
                    var created = await http.GetAsync("/orders/43");
                    Assert.Equal(36, created.Headers.GetValues("x-request-id").Single().Length);
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var inside = server.Events().First(item => item.GetProperty("type").GetString() == "log" && item.GetProperty("message").GetString() == "dentro");
                var log = RequestLogs(server).First();
                Assert.Equal("req-1", inside.GetProperty("requestId").GetString());
                Assert.Equal("req-1", log.GetProperty("requestId").GetString());
                Assert.Equal("u-7", log.GetProperty("user").GetProperty("id").GetString());
                var attrs = log.GetProperty("attrs");
                Assert.Equal("info", log.GetProperty("level").GetString());
                Assert.Equal("GET", attrs.GetProperty("method").GetString());
                Assert.Equal("/orders/{id}", attrs.GetProperty("route").GetString());
                Assert.Equal("/orders/42", attrs.GetProperty("path").GetString());
                Assert.Equal(200, attrs.GetProperty("status").GetInt32());
                Assert.True(attrs.GetProperty("durationMs").GetInt64() >= 0);
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task ReportsErrorsThatEscapeThePipelineWithTheMaskedRequest()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server);
                await using (var app = await Start(client))
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/boom?token=abcdefghijklmnop&page=2");
                    request.Headers.Add("Authorization", "Bearer abcdefghijklmnop");
                    request.Headers.Add("User-Agent", "teste/1.0");
                    await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().SendAsync(request));
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var error = Assert.Single(server.Events(), item => item.GetProperty("type").GetString() == "error");
                Assert.False(error.GetProperty("handled").GetBoolean());
                Assert.Equal("System.InvalidOperationException", error.GetProperty("exception").GetProperty("type").GetString());
                var details = error.GetProperty("request");
                Assert.Equal("POST", details.GetProperty("method").GetString());
                Assert.Equal("/boom", details.GetProperty("route").GetString());
                Assert.StartsWith("/boom?token=abcd", details.GetProperty("url").GetString());
                Assert.DoesNotContain("abcdefghijklmnop", details.GetProperty("url").GetString());
                Assert.StartsWith("Bearer abcd…", details.GetProperty("headers").GetProperty("authorization").GetString());
                Assert.StartsWith("abcd…", details.GetProperty("query").GetProperty("token").GetString());
                Assert.Equal("teste/1.0", details.GetProperty("userAgent").GetString());
                var log = Assert.Single(RequestLogs(server));
                Assert.Equal("error", log.GetProperty("level").GetString());
                Assert.Equal(500, log.GetProperty("attrs").GetProperty("status").GetInt32());
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task ReportsErrorsHandledByTheExceptionHandlerOnce()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server);
                await using (var app = await Start(client, pipeline: application => application.UseExceptionHandler(handler => handler.Run(context =>
                {
                    context.Response.StatusCode = 500;
                    return context.Response.WriteAsync("falhou");
                }))))
                {
                    var response = await app.GetTestClient().PostAsync("/boom", new StringContent(string.Empty));
                    Assert.Equal(500, (int)response.StatusCode);
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var error = Assert.Single(server.Events(), item => item.GetProperty("type").GetString() == "error");
                Assert.Equal("quebrou", error.GetProperty("exception").GetProperty("message").GetString());
                Assert.Equal(500, Assert.Single(RequestLogs(server)).GetProperty("attrs").GetProperty("status").GetInt32());
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task KeepsTheHandledErrorUnhandledWithTheRouteWhenTheLoggerSeesItFirst()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server);
                await using (var app = await Start(client, logToRadar: true, pipeline: application => application.UseExceptionHandler(handler => handler.Run(context =>
                {
                    context.Response.StatusCode = 500;
                    return context.Response.WriteAsync("falhou");
                }))))
                {
                    var response = await app.GetTestClient().PostAsync("/boom?token=abcdefghijklmnop", new StringContent(string.Empty));
                    Assert.Equal(500, (int)response.StatusCode);
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var error = Assert.Single(server.Events(), item => item.GetProperty("type").GetString() == "error");
                Assert.False(error.GetProperty("handled").GetBoolean());
                Assert.Equal("/boom", error.GetProperty("request").GetProperty("route").GetString());
                Assert.DoesNotContain("abcdefghijklmnop", error.GetProperty("request").GetProperty("url").GetString());
                Assert.Equal("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", error.GetProperty("attrs").GetProperty("logger").GetString());
                Assert.Equal(500, Assert.Single(RequestLogs(server)).GetProperty("attrs").GetProperty("status").GetInt32());
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task MasksSecretPathParamsAndSkipsHealthChecksAndExcludedPaths()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server, options => options.LogRequests.Exclude.Add("/orders"));
                await using (var app = await Start(client))
                {
                    var http = app.GetTestClient();
                    await http.GetAsync("/reset/s3cr3t-reset-token-value-123456");
                    await http.GetAsync("/healthz");
                    await http.GetAsync("/orders/9");
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var log = Assert.Single(RequestLogs(server));
                Assert.Equal("/reset/{token}", log.GetProperty("attrs").GetProperty("route").GetString());
                Assert.StartsWith("/reset/s3cr…", log.GetProperty("attrs").GetProperty("path").GetString());
                Assert.DoesNotContain("s3cr3t-reset-token-value", JsonSerializer.Serialize(server.Events()));
                client.Close(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task SendsOnlyMethodAndRouteWhenAskedAndRunsTheMiddlewareOnce()
        {
            using (var server = new FakeIngest())
            {
                var client = ClientFor(server, options => options.RequestDetail = RequestDetail.Route);
                await using (var app = await Start(client, useRadar: true))
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().PostAsync("/boom?token=abcdefghijklmnop", new StringContent(string.Empty)));
                    await app.GetTestClient().GetAsync("/orders/5");
                }
                Assert.True(client.Flush(TimeSpan.FromSeconds(5)));
                var error = Assert.Single(server.Events(), item => item.GetProperty("type").GetString() == "error");
                var details = error.GetProperty("request");
                Assert.Equal(new[] { "method", "url", "route" }, details.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.Equal("/boom", details.GetProperty("url").GetString());
                var logs = RequestLogs(server);
                Assert.Equal(2, logs.Count);
                Assert.All(logs, log => Assert.False(log.GetProperty("attrs").TryGetProperty("path", out _)));
                client.Close(TimeSpan.Zero);
            }
        }
    }
}
