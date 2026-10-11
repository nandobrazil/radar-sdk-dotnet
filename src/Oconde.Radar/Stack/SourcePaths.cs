using System;
using System.IO;

namespace Oconde.Radar.Stack
{
    internal static class SourcePaths
    {
        public static string Display(string path, string? directory)
        {
            if (path.StartsWith("/_/", StringComparison.Ordinal)) return path.Substring(3);
            if (string.IsNullOrEmpty(directory)) return path;
            var root = directory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.Ordinal) ? path.Substring(root.Length).Replace('\\', '/') : path;
        }
    }
}
