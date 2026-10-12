using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Oconde.Radar.Integration;

namespace Oconde.Radar.AspNetCore
{
    public sealed class RadarMiddleware
    {
        internal const string HandledKey = "Oconde.Radar.Middleware";
        private const string RequestIdHeader = "x-request-id";
        private const int ClientClosedRequest = 499;
        private readonly RequestDelegate next;
        private readonly RadarClient client;

        public RadarMiddleware(RequestDelegate next, RadarClient? client = null)
        {
            this.next = next;
            this.client = client ?? Radar.Client;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!client.IsActive || context.Items.ContainsKey(HandledKey))
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            context.Items[HandledKey] = true;
            RadarRequestScope scope;
            try
            {
                scope = client.BeginRequest(HttpContextReader.Request(context), context.Request.Headers[RequestIdHeader].FirstOrDefault());
            }
            catch (Exception)
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            using (scope)
            {
                var response = context.Response;
                response.OnStarting(() =>
                {
                    if (!response.Headers.ContainsKey(RequestIdHeader)) response.Headers[RequestIdHeader] = scope.RequestId;
                    return Task.CompletedTask;
                });
                try
                {
                    await next(context).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    scope.SetRoute(HttpContextReader.Route(context));
                    if (IsClientGone(context, error))
                    {
                        scope.Finish(ClientClosedRequest);
                        throw;
                    }
                    scope.CaptureException(error);
                    scope.Finish(500);
                    throw;
                }
                var handled = context.Features.Get<IExceptionHandlerFeature>();
                scope.SetRoute(handled != null ? HttpContextReader.RouteOfEndpoint(EndpointOf(handled)) ?? HttpContextReader.Route(context) : HttpContextReader.Route(context));
                if (handled?.Error != null) scope.CaptureException(handled.Error);
                scope.Finish(response.StatusCode);
            }
        }

        private static bool IsClientGone(HttpContext context, Exception error)
        {
            try
            {
                return (error is OperationCanceledException || error is IOException) && context.RequestAborted.IsCancellationRequested;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static object? EndpointOf(IExceptionHandlerFeature feature)
        {
            try
            {
                return feature.GetType().GetProperty("Endpoint")?.GetValue(feature, null);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
