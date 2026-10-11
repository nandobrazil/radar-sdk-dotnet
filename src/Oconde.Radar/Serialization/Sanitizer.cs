using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Oconde.Radar.Json;

namespace Oconde.Radar.Serialization
{
    internal static class Sanitizer
    {
        public const string LimitReached = "…[limite]";
        public const string Circular = "[circular]";
        public const string TooDeep = "[profundidade]";
        public const int MaxNodes = 2000;

        private static readonly object Skip = new object();
        private static readonly string[] DescribedNamespaces = { "System", "Microsoft", "Windows", "Castle.Proxies", "NHibernate", "Newtonsoft" };

        private sealed class Budget
        {
            public int Remaining = MaxNodes;
        }

        public static object? Sanitize(object? value)
        {
            var clean = Visit(value, 0, new HashSet<object>(ReferenceComparer.Instance), new Budget());
            return ReferenceEquals(clean, Skip) ? null : clean;
        }

        private static object? Visit(object? value, int depth, HashSet<object> seen, Budget budget)
        {
            if (value == null) return null;
            budget.Remaining--;
            var scalar = Scalar(value);
            if (!ReferenceEquals(scalar, Skip)) return scalar;
            if (IsSkipped(value)) return Skip;
            var type = value.GetType();
            if (!type.IsValueType && seen.Contains(value)) return Circular;
            if (depth >= Protocol.MaxDepth) return TooDeep;
            if (budget.Remaining <= 0) return LimitReached;
            if (!type.IsValueType) seen.Add(value);
            try
            {
                if (value is JsonObject record) return VisitDictionary(record.Select(entry => new KeyValuePair<object?, object?>(entry.Key, entry.Value)), depth, seen, budget);
                if (value is JsonArray array) return VisitSequence(array, depth, seen, budget);
                var request = RequestSummary(value);
                if (request != null) return request;
                if (value is IDictionary dictionary) return VisitDictionary(EntriesOf(dictionary), depth, seen, budget);
                var pairs = GenericEntries(value);
                if (pairs != null) return VisitDictionary(pairs, depth, seen, budget);
                if (value is IQueryable) return Describe(type);
                if (value is IEnumerable sequence) return VisitSequence(sequence, depth, seen, budget);
                if (IsDescribed(type)) return Describe(type);
                return VisitMembers(value, type, depth, seen, budget);
            }
            finally
            {
                if (!type.IsValueType) seen.Remove(value);
            }
        }

        private static object Scalar(object value)
        {
            switch (value)
            {
                case string text:
                    return Text.Truncate(text);
                case bool flag:
                    return flag;
                case char character:
                    return character.ToString();
                case Enum enumeration:
                    return enumeration.ToString();
                case byte[] bytes:
                    return "[binário " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes]";
                case double number:
                    return Number(number);
                case float number:
                    return Number(number);
                case decimal number:
                    return Math.Abs(number) <= Protocol.MaxSafeInteger ? (object)(double)number : number.ToString(CultureInfo.InvariantCulture);
                case ulong number:
                    return number <= (ulong)Protocol.MaxSafeInteger ? (object)(long)number : number.ToString(CultureInfo.InvariantCulture);
                case long number:
                    return Math.Abs(number) <= Protocol.MaxSafeInteger ? (object)number : number.ToString(CultureInfo.InvariantCulture);
                case int number:
                    return (long)number;
                case uint number:
                    return (long)number;
                case short number:
                    return (long)number;
                case ushort number:
                    return (long)number;
                case byte number:
                    return (long)number;
                case sbyte number:
                    return (long)number;
                case DateTime moment:
                    return moment.ToString("o", CultureInfo.InvariantCulture);
                case DateTimeOffset moment:
                    return moment.ToString("o", CultureInfo.InvariantCulture);
                case TimeSpan span:
                    return span.ToString();
                case Guid identifier:
                    return identifier.ToString();
                case Uri address:
                    return Text.Truncate(address.ToString());
                case Exception error:
                    return ExceptionSummary(error);
                default:
                    return Skip;
            }
        }

        private static object Number(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) return number.ToString(CultureInfo.InvariantCulture);
            return number;
        }

        private static JsonObject ExceptionSummary(Exception error)
        {
            string message;
            try
            {
                message = error.Message ?? string.Empty;
            }
            catch (Exception)
            {
                message = string.Empty;
            }
            var summary = new JsonObject();
            summary.Set("name", error.GetType().Name);
            summary.Set("message", Text.Truncate(message));
            return summary;
        }

        private static bool IsSkipped(object value) => value is Delegate || value is Type || value is MemberInfo || value is Assembly || value is Module || value is IntPtr || value is UIntPtr;

        private static bool IsDescribed(Type type)
        {
            if (IsAnonymous(type)) return false;
            var space = type.Namespace;
            if (space == null) return false;
            foreach (var prefix in DescribedNamespaces)
            {
                if (space == prefix || space.StartsWith(prefix + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsAnonymous(Type type) => type.Namespace == null && type.Name.Contains("AnonymousType");

        private static string Describe(Type type)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            return "<" + (tick > 0 ? name.Substring(0, tick) : name) + ">";
        }

        private static IEnumerable<KeyValuePair<object?, object?>> EntriesOf(IDictionary dictionary)
        {
            var enumerator = dictionary.GetEnumerator();
            while (enumerator.MoveNext()) yield return new KeyValuePair<object?, object?>(enumerator.Key, enumerator.Value);
        }

        private static IEnumerable<KeyValuePair<object?, object?>>? GenericEntries(object value)
        {
            var dictionaryInterface = value.GetType().GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && (candidate.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IDictionary`2" || candidate.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IReadOnlyDictionary`2"));
            if (dictionaryInterface == null || !(value is IEnumerable sequence)) return null;
            return Pairs(sequence);
        }

        private static IEnumerable<KeyValuePair<object?, object?>> Pairs(IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                if (item == null) continue;
                var type = item.GetType();
                var key = type.GetProperty("Key")?.GetValue(item, null);
                var entry = type.GetProperty("Value")?.GetValue(item, null);
                yield return new KeyValuePair<object?, object?>(key, entry);
            }
        }

        private static JsonObject VisitDictionary(IEnumerable<KeyValuePair<object?, object?>> entries, int depth, HashSet<object> seen, Budget budget)
        {
            var result = new JsonObject();
            foreach (var entry in entries)
            {
                if (budget.Remaining <= 0)
                {
                    result.Set("…", LimitReached);
                    break;
                }
                var clean = Visit(entry.Value, depth + 1, seen, budget);
                if (!ReferenceEquals(clean, Skip)) result.Set(KeyText(entry.Key), clean);
            }
            return result;
        }

        private static string KeyText(object? key)
        {
            if (key == null) return "null";
            var scalar = Scalar(key);
            return scalar is string text ? text : Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static JsonArray VisitSequence(IEnumerable sequence, int depth, HashSet<object> seen, Budget budget)
        {
            var items = new JsonArray();
            var enumerator = sequence.GetEnumerator();
            try
            {
                var taken = 0;
                while (taken < Protocol.ArrayItems && enumerator.MoveNext())
                {
                    if (budget.Remaining <= 0)
                    {
                        items.Add(LimitReached);
                        return items;
                    }
                    var clean = Visit(enumerator.Current, depth + 1, seen, budget);
                    items.Add(ReferenceEquals(clean, Skip) ? null : clean);
                    taken++;
                }
                if (taken == Protocol.ArrayItems)
                {
                    if (sequence is ICollection collection && collection.Count > Protocol.ArrayItems) items.Add("…[mais " + (collection.Count - Protocol.ArrayItems).ToString(CultureInfo.InvariantCulture) + "]");
                    else if (!(sequence is ICollection) && enumerator.MoveNext()) items.Add("…[mais]");
                }
                return items;
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        private static JsonObject VisitMembers(object value, Type type, int depth, HashSet<object> seen, Budget budget)
        {
            var result = new JsonObject();
            foreach (var member in Members(type))
            {
                if (budget.Remaining <= 0)
                {
                    result.Set("…", LimitReached);
                    break;
                }
                object? item;
                try
                {
                    item = member.Value(value);
                }
                catch (Exception)
                {
                    continue;
                }
                var clean = Visit(item, depth + 1, seen, budget);
                if (!ReferenceEquals(clean, Skip)) result.Set(member.Name, clean);
            }
            return result;
        }

        private sealed class Member
        {
            public Member(string name, Func<object, object?> value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }
            public Func<object, object?> Value { get; }
        }

        private static IEnumerable<Member> Members(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null) continue;
                var getter = property;
                yield return new Member(property.Name, target => getter.GetValue(target, null));
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var reader = field;
                yield return new Member(field.Name, target => reader.GetValue(target));
            }
        }

        private static readonly string[] RequestSuffixes = { "Request", "RequestBase", "RequestWrapper", "RequestMessage" };
        private static readonly string[] ContextSuffixes = { "HttpContext", "HttpContextBase", "HttpContextWrapper" };

        private static JsonObject? RequestSummary(object value)
        {
            var type = value.GetType();
            if (HasSuffix(type, ContextSuffixes))
            {
                var request = Read(value, "Request");
                return request != null && HasSuffix(request.GetType(), RequestSuffixes) ? Summarize(request) : null;
            }
            return HasSuffix(type, RequestSuffixes) ? Summarize(value) : null;
        }

        private static JsonObject? Summarize(object request)
        {
            var method = (Read(request, "HttpMethod") ?? Read(request, "Method"))?.ToString();
            var path = Read(request, "Path")?.ToString() ?? (Read(request, "RequestUri") as Uri)?.AbsolutePath ?? (Read(request, "Url") as Uri)?.AbsolutePath;
            if (method == null || path == null) return null;
            var summary = new JsonObject();
            summary.Set("method", Text.Truncate(method, 32));
            summary.Set("path", Text.Truncate(path));
            return summary;
        }

        private static bool HasSuffix(Type type, string[] suffixes)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var suffix in suffixes)
                {
                    if (current.Name.EndsWith(suffix, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private static object? Read(object target, string name)
        {
            try
            {
                var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return property == null || property.GetIndexParameters().Length > 0 ? null : property.GetValue(target, null);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
