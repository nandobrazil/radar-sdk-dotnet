using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Oconde.Radar.Context;
using Oconde.Radar.Json;
using Oconde.Radar.Redaction;
using Oconde.Radar.Serialization;

namespace Oconde.Radar.Integration
{
    internal static class RequestInfo
    {
        private static readonly Regex Placeholder = new Regex(@"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)");

        public static string? NormalizeRoute(string? route)
        {
            if (string.IsNullOrEmpty(route)) return null;
            return route!.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route;
        }

        public static string? RouteOf(RadarContext context)
        {
            if (context.Route != null || context.Request == null) return context.Route;
            try
            {
                return NormalizeRoute(context.Request.RoutePattern?.Invoke());
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Freeze(RadarContext context)
        {
            var request = context.Request;
            if (request == null) return;
            try
            {
                context.Route = RouteOf(context);
                var values = ReadRouteValues(request);
                var ip = ReadClientIp(request);
                request.RouteValues = () => values;
                request.ClientIp = () => ip;
                request.RoutePattern = null;
            }
            catch (Exception)
            {
                request.RouteValues = null;
                request.ClientIp = null;
                request.RoutePattern = null;
            }
        }

        private static string? ReadClientIp(RadarRequest request)
        {
            try
            {
                return request.ClientIp?.Invoke();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IDictionary<string, string>? ReadRouteValues(RadarRequest request)
        {
            try
            {
                return request.RouteValues?.Invoke();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static IDictionary<string, string> PathParameters(RadarRequest request, string? route)
        {
            var values = ReadRouteValues(request);
            var result = new Dictionary<string, string>();
            if (values == null) return result;
            var names = route == null ? null : new HashSet<string>(Placeholder.Matches(route).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (value.Value == null || (names != null && !names.Contains(value.Key))) continue;
                result[value.Key] = Text.Truncate(value.Value);
            }
            return result;
        }

        public static string MaskedPath(RadarRequest request, string? route, RedactMode mode) => Redactor.RedactPath(request.Path, PathParameters(request, route), mode);

        public static JsonObject Build(RadarRequest request, string? route, RedactMode mode, RequestDetail detail)
        {
            var method = request.Method.ToUpperInvariant();
            var info = new JsonObject();
            info.Set("method", method);
            if (detail == RequestDetail.Route)
            {
                info.Set("url", route ?? string.Empty);
                if (route != null) info.Set("route", route);
                return info;
            }
            var parameters = PathParameters(request, route);
            var query = request.QueryString.StartsWith("?", StringComparison.Ordinal) ? request.QueryString.Substring(1) : request.QueryString;
            info.Set("url", Text.Truncate(Redactor.RedactUrl(Redactor.RedactPath(request.Path, parameters, mode) + (query.Length > 0 ? "?" + query : string.Empty), mode)));
            if (route != null) info.Set("route", route);
            if (parameters.Count > 0) info.Set("params", Record(parameters.Select(parameter => new KeyValuePair<string, object?>(parameter.Key, parameter.Value)), mode));
            var pairs = Redactor.ParseQuery(query).GroupBy(pair => pair.Key).Select(group => new KeyValuePair<string, object?>(group.Key, group.Count() == 1 ? (object)group.First().Value : group.Select(pair => (object?)pair.Value).ToList())).ToList();
            if (pairs.Count > 0) info.Set("query", Record(pairs, mode));
            info.Set("headers", Headers(request.Headers, mode));
            var ip = ReadClientIp(request);
            if (!string.IsNullOrEmpty(ip)) info.Set("ip", ip);
            if (request.Headers.TryGetValue("user-agent", out var userAgent) && !string.IsNullOrEmpty(userAgent)) info.Set("userAgent", Text.Truncate(userAgent, 500));
            return info;
        }

        private static JsonObject Record(IEnumerable<KeyValuePair<string, object?>> entries, RedactMode mode)
        {
            var record = new JsonObject();
            foreach (var entry in entries) record.Set(entry.Key, entry.Value);
            return Redactor.RedactDeep(Sanitizer.Sanitize(record), mode) as JsonObject ?? new JsonObject();
        }

        private static JsonObject Headers(IDictionary<string, string> headers, RedactMode mode)
        {
            var result = new JsonObject();
            foreach (var header in headers)
            {
                if (result.Count >= Protocol.MaxHeaders) break;
                var name = header.Key.ToLowerInvariant();
                var value = header.Value ?? string.Empty;
                result.Set(name, mode == RedactMode.Mask && (Redactor.IsSensitiveHeader(name) || Redactor.LooksLikeCredential(value)) ? Redactor.MaskValue(value) : Text.Truncate(value));
            }
            return result;
        }
    }
}
