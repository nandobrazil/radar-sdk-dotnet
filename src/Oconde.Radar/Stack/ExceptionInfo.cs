using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Oconde.Radar.Json;
using Oconde.Radar.Serialization;

namespace Oconde.Radar.Stack
{
    internal static class ExceptionInfo
    {
        private const int MaxGroupDepth = 10;

        public static JsonObject Describe(Exception error)
        {
            string? directory;
            try
            {
                directory = Environment.CurrentDirectory;
            }
            catch (Exception)
            {
                directory = null;
            }
            return Describe(error, directory, 0, new HashSet<object>(ReferenceComparer.Instance));
        }

        private static JsonObject Describe(Exception error, string? directory, int depth, HashSet<object> seen)
        {
            var leaves = new List<Exception>();
            if (IsAggregate(error)) CollectLeaves(error, leaves, 0);
            if (leaves.Count == 1 && !seen.Contains(leaves[0])) return Describe(leaves[0], directory, depth, seen);
            seen.Add(error);
            var info = new JsonObject();
            info.Set("type", TypeNames.Pretty(error.GetType()));
            info.Set("message", Text.Truncate(SafeMessage(error), Protocol.MessageLength));
            info.Set("frames", Frames(error, directory));
            var cause = leaves.Count > 1 ? leaves[0] : error.InnerException;
            if (cause != null && depth < Protocol.CauseDepth && !seen.Contains(cause)) info.Set("cause", Describe(cause, directory, depth + 1, seen));
            return info;
        }

        private static bool IsAggregate(Exception error)
        {
            for (var type = error.GetType(); type != null; type = type.BaseType)
            {
                if (type.FullName == "System.AggregateException") return true;
            }
            return false;
        }

        private static void CollectLeaves(Exception error, List<Exception> leaves, int depth)
        {
            if (leaves.Count > 1) return;
            if (depth >= MaxGroupDepth || !IsAggregate(error))
            {
                leaves.Add(error);
                return;
            }
            var inner = error.GetType().GetProperty("InnerExceptions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(error, null) as IEnumerable;
            if (inner == null) return;
            foreach (var item in inner.OfType<Exception>())
            {
                CollectLeaves(item, leaves, depth + 1);
                if (leaves.Count > 1) return;
            }
        }

        private static string SafeMessage(Exception error)
        {
            try
            {
                return error.Message ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static JsonArray Frames(Exception error, string? directory)
        {
            var result = new JsonArray();
            StackFrame[]? frames;
            try
            {
                frames = new StackTrace(error, true).GetFrames();
            }
            catch (Exception)
            {
                frames = null;
            }
            if (frames == null) return result;
            var withContext = 0;
            foreach (var frame in frames.Take(Protocol.MaxFrames))
            {
                var entry = Frame(frame, directory, withContext < Protocol.ContextFrames);
                if (entry.ContainsKey("context")) withContext++;
                result.Add(entry);
            }
            return result;
        }

        private static JsonObject Frame(StackFrame frame, string? directory, bool wantsContext)
        {
            MethodBase? method = null;
            string? file = null;
            var line = 0;
            var column = 0;
            try
            {
                method = frame.GetMethod();
                file = frame.GetFileName();
                line = frame.GetFileLineNumber();
                column = frame.GetFileColumnNumber();
            }
            catch (Exception)
            {
            }
            var inApp = method != null && InApp.Contains(method);
            var entry = new JsonObject();
            entry.Set("fn", method == null ? "?" : SafeName(method));
            entry.Set("file", string.IsNullOrEmpty(file) ? ModuleName(method) : SourcePaths.Display(file!, directory));
            if (line > 0) entry.Set("line", (long)line);
            if (column > 0) entry.Set("col", (long)column);
            entry.Set("inApp", inApp);
            if (inApp && wantsContext && !string.IsNullOrEmpty(file) && line > 0)
            {
                var context = SourceContext.Around(file!, line);
                if (context != null) entry.Set("context", context);
            }
            return entry;
        }

        private static string SafeName(MethodBase method)
        {
            try
            {
                return FrameNames.Of(method);
            }
            catch (Exception)
            {
                return method.Name;
            }
        }

        private static string ModuleName(MethodBase? method)
        {
            try
            {
                return method?.Module.Name ?? "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
