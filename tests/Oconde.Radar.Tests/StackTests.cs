using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oconde.Radar.Json;
using Oconde.Radar.Stack;
using Xunit;

namespace Oconde.Radar.Tests
{
    public class StackTests
    {
        private static readonly string Source = FindSource();

        private static string FindSource()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SampleErrors.cs"))) directory = directory.Parent;
            return directory == null ? string.Empty : File.ReadAllText(Path.Combine(directory.FullName, "SampleErrors.cs"));
        }

        private static int LineOf(string text) => Source.Split('\n').Select((line, index) => new { line, index }).First(item => item.line.Contains(text)).index + 1;

        private static JsonObject Info(Exception error) => ExceptionInfo.Describe(error);

        private static List<JsonObject> Frames(JsonObject info) => ((JsonArray)info["frames"]!).Cast<JsonObject>().ToList();

        [Fact]
        public void FramesGoFromTheInnermostWithFileLineAndTheCodeAround()
        {
            var info = Info(SampleErrors.Caught(SampleErrors.LoadOrder));
            Assert.Equal("System.Collections.Generic.KeyNotFoundException", info["type"]);
            Assert.Equal(false, Frames(info)[0]["inApp"]);
            var appFrames = Frames(info).Where(frame => (bool)frame["inApp"]!).ToList();
            var first = appFrames[0];
            Assert.Equal("Oconde.Radar.Tests.SampleErrors.Checkout.Total", first["fn"]);
            Assert.EndsWith("SampleErrors.cs", (string)first["file"]!);
            Assert.Equal((long)LineOf("return order[\"total\"];"), first["line"]);
            Assert.Equal(true, first["inApp"]);
            var context = (JsonObject)first["context"]!;
            Assert.Contains("return order[\"total\"];", (string)context["line"]!);
            Assert.Contains("public int Total(", string.Join("\n", ((JsonArray)context["pre"]!).Cast<string>().ToArray()));
            Assert.Equal("Oconde.Radar.Tests.SampleErrors.LoadOrder", appFrames[1]["fn"]);
        }

        [Fact]
        public void NamesLambdasByTheMethodThatHoldsThem()
        {
            var names = Frames(Info(SampleErrors.Caught(SampleErrors.Lambda))).Select(frame => (string)frame["fn"]!).ToList();
            Assert.Contains("Oconde.Radar.Tests.SampleErrors.Lambda.lambda", names);
        }

#if !NET48
        [Fact]
        public void NamesAsyncMethodsByTheirOwnName()
        {
            var error = SampleErrors.Caught(() => SampleErrors.LoadOrderAsync().GetAwaiter().GetResult());
            var names = Frames(Info(error)).Select(frame => (string)frame["fn"]!).ToList();
            Assert.Contains("Oconde.Radar.Tests.SampleErrors.LoadOrderAsync", names);
            Assert.DoesNotContain(names, name => name.Contains("MoveNext") || name.Contains("d__"));
        }
#endif

        [Fact]
        public void KeepsTheCauseChainAndUnwrapsAggregates()
        {
            var wrapped = Info(SampleErrors.Caught(SampleErrors.Wrapped));
            Assert.Equal("System.InvalidOperationException", wrapped["type"]);
            Assert.Equal("System.Collections.Generic.KeyNotFoundException", ((JsonObject)wrapped["cause"]!)["type"]);
            var inner = SampleErrors.Caught(SampleErrors.LoadOrder);
            var single = Info(new AggregateException(new AggregateException(inner)));
            Assert.Equal("System.Collections.Generic.KeyNotFoundException", single["type"]);
            var many = Info(new AggregateException("várias falhas", inner, new InvalidOperationException("outra")));
            Assert.Equal("System.AggregateException", many["type"]);
            Assert.Equal("System.Collections.Generic.KeyNotFoundException", ((JsonObject)many["cause"]!)["type"]);
        }

        [Fact]
        public void StopsOnACycleOfInnerExceptions()
        {
            var first = new InvalidOperationException("um");
            var second = new InvalidOperationException("dois", first);
            typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(first, second);
            var info = Info(second);
            var depth = 0;
            for (var node = info; node != null; node = node["cause"] as JsonObject) depth++;
            Assert.True(depth <= 6);
        }

        [Fact]
        public void LimitsFramesAndMessageAndSurvivesAMessageThatThrows()
        {
            var deep = Info(SampleErrors.Caught(() => SampleErrors.Recurse(80)));
            Assert.Equal(50, Frames(deep).Count);
            Assert.Equal("Oconde.Radar.Tests.SampleErrors.Recurse", Frames(deep)[0]["fn"]);
            Assert.EndsWith("…[cortado]", (string)Info(new InvalidOperationException(new string('x', 3000)))["message"]!);
            var broken = Info(new SampleErrors.BrokenMessageException());
            Assert.Equal("Oconde.Radar.Tests.SampleErrors.BrokenMessageException", broken["type"]);
            Assert.Equal(string.Empty, broken["message"]);
        }

        [Fact]
        public void FrameworkFramesAreNotAppCodeAndHaveNoContext()
        {
            var frames = Frames(Info(SampleErrors.Caught(() => SampleErrors.ParseInvalid())));
            var framework = frames.Where(frame => ((string)frame["fn"]!).StartsWith("System.", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(framework);
            Assert.All(framework, frame => Assert.Equal(false, frame["inApp"]));
            Assert.All(framework, frame => Assert.False(frame.ContainsKey("context")));
            Assert.Contains(frames, frame => (string)frame["fn"]! == "Oconde.Radar.Tests.SampleErrors.ParseInvalid" && (bool)frame["inApp"]!);
        }

        [Theory]
        [InlineData("/_/src/App/Orders.cs", "src/App/Orders.cs")]
        [InlineData("/_/Orders.cs", "Orders.cs")]
        [InlineData("C:\\build\\src\\Orders.cs", "C:\\build\\src\\Orders.cs")]
        public void ShortensDeterministicBuildPaths(string path, string expected) => Assert.Equal(expected, SourcePaths.Display(path, "/somewhere/else"));
    }
}
