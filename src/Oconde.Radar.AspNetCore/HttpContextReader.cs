using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Routing;
#endif
using Oconde.Radar.Integration;

namespace Oconde.Radar.AspNetCore
{
    internal static class HttpContextReader
    {
        private const int MaxHeadersRead = 200;

        public static RadarRequest Request(HttpContext context)
        {
            var request = context.Request;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                if (headers.Count >= MaxHeadersRead) break;
                headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value.ToArray());
            }
            return new RadarRequest(request.Method, (request.PathBase.Value ?? string.Empty) + (request.Path.Value ?? string.Empty))
            {
                QueryString = request.QueryString.HasValue ? request.QueryString.Value!.TrimStart('?') : string.Empty,
                Headers = headers,
                ClientIp = () => context.Connection.RemoteIpAddress?.ToString(),
                RouteValues = () => RouteValues(context),
                RoutePattern = () => Route(context),
            };
        }

        public static string? Route(HttpContext context)
        {
            try
            {
#if NET8_0_OR_GREATER
                return (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
#else
                foreach (var feature in context.Features)
                {
                    if (feature.Key.FullName != "Microsoft.AspNetCore.Http.Features.IEndpointFeature") continue;
                    return RouteOfEndpoint(feature.Key.GetProperty("Endpoint")?.GetValue(feature.Value, null));
                }
                return null;
#endif
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string? RouteOfEndpoint(object? endpoint)
        {
            try
            {
                var pattern = endpoint?.GetType().GetProperty("RoutePattern")?.GetValue(endpoint, null);
                return pattern?.GetType().GetProperty("RawText")?.GetValue(pattern, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IDictionary<string, string>? RouteValues(HttpContext context)
        {
#if NET8_0_OR_GREATER
            return Strings(context.Request.RouteValues);
#else
            var values = context.Request.GetType().GetProperty("RouteValues")?.GetValue(context.Request, null) as IEnumerable<KeyValuePair<string, object>>;
            if (values != null) return Strings(values.Select(value => new KeyValuePair<string, object?>(value.Key, value.Value)));
            foreach (var feature in context.Features)
            {
                if (feature.Key.FullName != "Microsoft.AspNetCore.Routing.IRoutingFeature") continue;
                var data = feature.Key.GetProperty("RouteData")?.GetValue(feature.Value, null);
                var routeValues = data?.GetType().GetProperty("Values")?.GetValue(data, null) as IEnumerable<KeyValuePair<string, object>>;
                return routeValues == null ? null : Strings(routeValues.Select(value => new KeyValuePair<string, object?>(value.Key, value.Value)));
            }
            return null;
#endif
        }

        private static IDictionary<string, string>? Strings(IEnumerable<KeyValuePair<string, object?>>? values)
        {
            if (values == null) return null;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (value.Value == null) continue;
                result[value.Key] = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            return result;
        }
    }
}
