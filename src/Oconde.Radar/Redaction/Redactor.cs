using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Oconde.Radar.Json;

namespace Oconde.Radar.Redaction
{
    internal static class Redactor
    {
        private const int MaxRedactDepth = 8;
        private static readonly HashSet<string> SecretWords = new HashSet<string>
        {
            "pass", "passwd", "password", "passwords", "senha", "secret", "secrets", "token", "tokens", "authorization", "auth", "cookie", "cookies", "apikey", "privatekey",
            "cpf", "card", "cvv", "cvc", "pin", "otp", "signature", "jwt", "session", "sessionid", "credential", "credentials",
        };
        private static readonly string[][] SecretPairs = { new[] { "api", "key" }, new[] { "private", "key" }, new[] { "access", "key" }, new[] { "secret", "key" } };
        private static readonly HashSet<string> LowEntropyWords = new HashSet<string> { "pass", "passwd", "password", "passwords", "senha", "cpf", "card", "cvv", "cvc", "pin", "otp" };
        private static readonly Regex CustomSecretHeader = new Regex("^x-.+-(key|secret|token)$", RegexOptions.IgnoreCase);
        private static readonly Regex AuthScheme = new Regex(@"^(Bearer|Basic|Token|Digest|ApiKey)\s+", RegexOptions.IgnoreCase);
        private static readonly Regex JwtPrefix = new Regex(@"^eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.");
        private static readonly Regex CamelBoundary = new Regex("([a-z0-9])([A-Z])");
        private static readonly Regex WordSeparator = new Regex("[^a-z0-9]+");
        private static byte[] fingerprintKey = RandomKey();

        public static void SetFingerprintSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                fingerprintKey = RandomKey();
                return;
            }
            using (var sha = SHA256.Create()) fingerprintKey = sha.ComputeHash(Encoding.UTF8.GetBytes("@oconde/radar:" + secret));
        }

        public static string Fingerprint(string value)
        {
            using (var hmac = new HMACSHA256(fingerprintKey))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder();
                for (var index = 0; index < 4; index++) builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        public static bool IsSensitiveKey(string key)
        {
            var parts = Words(key);
            if (parts.Count == 1 && parts[0] == "key") return true;
            if (parts.Any(SecretWords.Contains)) return true;
            for (var index = 0; index < parts.Count - 1; index++)
            {
                foreach (var pair in SecretPairs)
                {
                    if (parts[index] == pair[0] && parts[index + 1] == pair[1]) return true;
                }
            }
            return false;
        }

        public static bool IsLowEntropyKey(string key) => Words(key).Any(LowEntropyWords.Contains);

        public static bool IsSensitiveHeader(string name) => IsSensitiveKey(name) || CustomSecretHeader.IsMatch(name);

        public static bool LooksLikeCredential(string value)
        {
            var head = value.Length > 64 ? value.Substring(0, 64) : value;
            return AuthScheme.IsMatch(head.Length > 16 ? head.Substring(0, 16) : head) || JwtPrefix.IsMatch(head);
        }

        public static string MaskValue(object? value, bool revealEdges = true)
        {
            var text = value as string ?? JsonWriter.Write(value);
            var scheme = AuthScheme.Match(text.Length > 16 ? text.Substring(0, 16) : text);
            var prefix = scheme.Success ? scheme.Groups[1].Value + " " : string.Empty;
            var secret = scheme.Success ? text.Substring(scheme.Length) : text;
            var visible = revealEdges && secret.Length > 12 ? secret.Substring(0, 4) + "…" + secret.Substring(secret.Length - 4) : "•••";
            return prefix + visible + " #" + Fingerprint(text);
        }

        public static object? RedactDeep(object? value, RedactMode mode, int depth = 0)
        {
            if (mode == RedactMode.None || depth > MaxRedactDepth || value == null) return value;
            switch (value)
            {
                case string text:
                    return LooksLikeCredential(text) ? MaskValue(text) : text;
                case JsonArray items:
                    var array = new JsonArray();
                    foreach (var item in items) array.Add(RedactDeep(item, mode, depth + 1));
                    return array;
                case JsonObject record:
                    var result = new JsonObject();
                    foreach (var entry in record)
                    {
                        var hasValue = entry.Value != null && !(entry.Value is string empty && empty.Length == 0);
                        result.Set(entry.Key, IsSensitiveKey(entry.Key) && hasValue ? MaskValue(entry.Value, !IsLowEntropyKey(entry.Key)) : RedactDeep(entry.Value, mode, depth + 1));
                    }
                    return result;
                default:
                    return value;
            }
        }

        public static string RedactUrl(string url, RedactMode mode)
        {
            var queryStart = url.IndexOf('?');
            if (mode == RedactMode.None || queryStart < 0) return url;
            var parameters = ParseQuery(url.Substring(queryStart + 1));
            var changed = false;
            foreach (var parameter in parameters.ToList())
            {
                if (!IsSensitiveKey(parameter.Key) || parameter.Value.Length == 0) continue;
                SetParameter(parameters, parameter.Key, MaskValue(parameter.Value, !IsLowEntropyKey(parameter.Key)));
                changed = true;
            }
            if (!changed) return url;
            return url.Substring(0, queryStart) + "?" + string.Join("&", parameters.Select(parameter => FormEncode(parameter.Key) + "=" + FormEncode(parameter.Value)).ToArray());
        }

        public static string RedactPath(string path, IDictionary<string, string>? parameters, RedactMode mode)
        {
            if (mode == RedactMode.None || parameters == null) return path;
            var result = path;
            foreach (var parameter in parameters)
            {
                var value = parameter.Value;
                if (value == null || value.Length < 4 || !IsSensitiveKey(parameter.Key)) continue;
                var masked = MaskValue(value, !IsLowEntropyKey(parameter.Key));
                var encoded = EncodeUriComponent(value);
                result = result.Replace(value, masked);
                if (encoded != value) result = result.Replace(encoded, masked);
            }
            return result;
        }

        private static List<string> Words(string name) => WordSeparator.Split(CamelBoundary.Replace(name, "$1 $2").ToLowerInvariant()).Where(part => part.Length > 0).ToList();

        private static byte[] RandomKey()
        {
            var key = new byte[32];
            var random = RandomNumberGenerator.Create();
            random.GetBytes(key);
            (random as IDisposable)?.Dispose();
            return key;
        }

        internal static List<KeyValuePair<string, string>> ParseQuery(string query)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var segment in query.Split('&'))
            {
                if (segment.Length == 0) continue;
                var equals = segment.IndexOf('=');
                var name = equals >= 0 ? segment.Substring(0, equals) : segment;
                var value = equals >= 0 ? segment.Substring(equals + 1) : string.Empty;
                result.Add(new KeyValuePair<string, string>(FormDecode(name), FormDecode(value)));
            }
            return result;
        }

        private static void SetParameter(List<KeyValuePair<string, string>> parameters, string name, string value)
        {
            var first = parameters.FindIndex(parameter => parameter.Key == name);
            if (first < 0)
            {
                parameters.Add(new KeyValuePair<string, string>(name, value));
                return;
            }
            parameters[first] = new KeyValuePair<string, string>(name, value);
            for (var index = parameters.Count - 1; index > first; index--)
            {
                if (parameters[index].Key == name) parameters.RemoveAt(index);
            }
        }

        private static string FormDecode(string text)
        {
            var bytes = new List<byte>();
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '+')
                {
                    bytes.Add(0x20);
                }
                else if (character == '%' && index + 2 < text.Length && IsHex(text[index + 1]) && IsHex(text[index + 2]))
                {
                    bytes.Add(byte.Parse(text.Substring(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 2;
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
                }
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static bool IsHex(char character) => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F');

        private static string FormEncode(string text)
        {
            var builder = new StringBuilder();
            foreach (var value in Encoding.UTF8.GetBytes(text))
            {
                var character = (char)value;
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '*' || character == '-' || character == '.' || character == '_') builder.Append(character);
                else if (value == 0x20) builder.Append('+');
                else builder.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        internal static string EncodeUriComponent(string text)
        {
            var builder = new StringBuilder();
            foreach (var value in Encoding.UTF8.GetBytes(text))
            {
                var character = (char)value;
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || "-_.!~*'()".IndexOf(character) >= 0) builder.Append(character);
                else builder.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
