using System;

namespace Oconde.Radar.Stack
{
    internal static class TypeNames
    {
        public static string Pretty(Type type)
        {
            var name = WithoutArity(type.Name);
            var prefix = type.IsNested && type.DeclaringType != null ? Pretty(type.DeclaringType) : type.Namespace;
            return string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
        }

        public static string WithoutArity(string name)
        {
            var tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }
    }
}
