using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Oconde.Radar.Stack
{
    internal static class FrameNames
    {
        public static string Of(MethodBase method)
        {
            var type = method.DeclaringType;
            var name = method.Name;
            if (type != null && name == "MoveNext" && IsCompilerGenerated(type) && TryHolder(type.Name, out var original))
            {
                name = original;
                type = type.DeclaringType;
            }
            else if (TryHolder(name, out var holder))
            {
                name = holder + "." + LocalName(method.Name);
            }
            while (type != null && IsCompilerGenerated(type))
            {
                if (TryHolder(type.Name, out var enclosing) && type.Name.Contains(">d__")) name = enclosing + "." + name;
                type = type.DeclaringType;
            }
            return type == null ? name : TypeNames.Pretty(type) + "." + name;
        }

        private static string LocalName(string generated)
        {
            var marker = generated.IndexOf(">g__", StringComparison.Ordinal);
            if (marker < 0) return "lambda";
            var start = marker + 4;
            var end = generated.IndexOf('|', start);
            return end > start ? generated.Substring(start, end - start) : "lambda";
        }

        private static bool TryHolder(string generated, out string holder)
        {
            holder = string.Empty;
            if (!generated.StartsWith("<", StringComparison.Ordinal)) return false;
            var close = generated.IndexOf('>');
            if (close <= 1) return false;
            holder = generated.Substring(1, close - 1);
            return true;
        }

        private static bool IsCompilerGenerated(Type type) => type.Name.StartsWith("<", StringComparison.Ordinal) || type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }
}
