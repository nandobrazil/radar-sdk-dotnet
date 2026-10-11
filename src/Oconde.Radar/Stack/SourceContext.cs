using System;
using System.IO;
using Oconde.Radar.Json;

namespace Oconde.Radar.Stack
{
    internal static class SourceContext
    {
        private const long MaxFileBytes = 2_000_000;

        public static JsonObject? Around(string file, int line)
        {
            string[] lines;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > MaxFileBytes) return null;
                lines = File.ReadAllLines(file);
            }
            catch (Exception)
            {
                return null;
            }
            var index = line - 1;
            if (index < 0 || index >= lines.Length) return null;
            var pre = new JsonArray();
            for (var position = Math.Max(0, index - Protocol.ContextLines); position < index; position++) pre.Add(Clip(lines[position]));
            var post = new JsonArray();
            for (var position = index + 1; position < Math.Min(lines.Length, index + 1 + Protocol.ContextLines); position++) post.Add(Clip(lines[position]));
            var context = new JsonObject();
            context.Set("pre", pre);
            context.Set("line", Clip(lines[index]));
            context.Set("post", post);
            return context;
        }

        private static string Clip(string text) => text.Length > Protocol.ContextLineLength ? text.Substring(0, Protocol.ContextLineLength) + "…" : text;
    }
}
