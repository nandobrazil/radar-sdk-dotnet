using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oconde.Radar.Json;
using Oconde.Radar.Serialization;
using Xunit;

namespace Oconde.Radar.Tests
{
    public class SerializationTests
    {
        public enum Color
        {
            Red
        }

        public sealed class Order
        {
            public int Id { get; set; }
            public decimal Total { get; set; }
            public string Secret => throw new InvalidOperationException("no");
            public Order? Parent { get; set; }
        }

        public sealed class FakeHttpRequest
        {
            public string Method => "POST";
            public string Path => "/pagar";
            public string QueryString => "?api_key=sk_live_51HxQabcdefghijk";
            public IDictionary Headers => new Hashtable { ["Authorization"] = "Bearer abcdefghijklmnop" };
        }

        private static string Write(object? value) => JsonWriter.Write(Sanitizer.Sanitize(value));

        [Fact]
        public void WritesCompactJsonLikeJavaScript()
        {
            var record = new JsonObject();
            record.Set("text", "aspas \" barra \\ linha\n\u0001 acento ç \ud800");
            record.Set("int", 42L);
            record.Set("double", 1.5);
            record.Set("whole", 100.0);
            record.Set("big", 1e21);
            record.Set("small", 1e-7);
            record.Set("list", new JsonArray { true, null, "x" });
            Assert.Equal("{\"text\":\"aspas \\\" barra \\\\ linha\\n\\u0001 acento ç \\ud800\",\"int\":42,\"double\":1.5,\"whole\":100,\"big\":1e+21,\"small\":1e-7,\"list\":[true,null,\"x\"]}", JsonWriter.Write(record));
        }

        [Fact]
        public void TurnsDotNetValuesIntoJsonSafeData()
        {
            var when = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            var json = Write(new Dictionary<object, object?>
            {
                ["when"] = when,
                ["day"] = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                ["id"] = new Guid("12345678-1234-5678-1234-567812345678"),
                ["price"] = 10.50m,
                ["color"] = Color.Red,
                ["blob"] = new byte[] { 0, 1 },
                ["nan"] = double.NaN,
                ["huge"] = long.MaxValue,
                ["error"] = new InvalidOperationException("ruim"),
                ["duration"] = TimeSpan.FromSeconds(90),
                [7] = "chave numérica",
            });
            Assert.Equal(
                "{\"when\":\"2026-10-10T12:00:00.0000000+00:00\",\"day\":\"2026-10-10T00:00:00.0000000Z\",\"id\":\"12345678-1234-5678-1234-567812345678\",\"price\":10.5,\"color\":\"Red\",\"blob\":\"[binário 2 bytes]\",\"nan\":\"NaN\",\"huge\":\"9223372036854775807\",\"error\":{\"name\":\"InvalidOperationException\",\"message\":\"ruim\"},\"duration\":\"00:01:30\",\"7\":\"chave numérica\"}",
                json);
        }

        [Fact]
        public void WalksAnonymousObjectsAndAppTypesSkippingGettersThatThrow()
        {
            var order = new Order { Id = 1, Total = 2m };
            order.Parent = order;
            Assert.Equal("{\"pedido\":{\"Id\":1,\"Total\":2,\"Parent\":\"[circular]\"},\"tags\":[\"a\",\"b\"]}", Write(new { pedido = order, tags = new[] { "a", "b" } }));
        }

        [Fact]
        public void DescribesFrameworkObjectsInsteadOfWalkingThemAndReducesRequestsToMethodAndPath()
        {
            using (var stream = new MemoryStream())
            {
                var json = Write(new { stream, request = new FakeHttpRequest(), action = (Action)(() => { }), type = typeof(string) });
                Assert.Equal("{\"stream\":\"<MemoryStream>\",\"request\":{\"method\":\"POST\",\"path\":\"/pagar\"}}", json);
            }
        }

        [Fact]
        public void LimitsStringsDepthItemsAndTheNodeBudget()
        {
            Assert.Equal("\"" + new string('a', 2000) + "…[cortado]\"", Write(new string('a', 2001)));
            var deep = new Dictionary<string, object?>();
            var node = deep;
            for (var level = 0; level < 10; level++)
            {
                var next = new Dictionary<string, object?>();
                node["next"] = next;
                node = next;
            }
            Assert.Contains("[profundidade]", Write(deep));
            var items = Write(Enumerable.Range(0, 60).ToList());
            Assert.EndsWith(",\"…[mais 10]\"]", items);
            var wide = Enumerable.Range(0, 3000).ToDictionary(index => "k" + index, index => (object)index);
            Assert.Contains("\"…\":\"…[limite]\"", Write(wide));
        }

        [Fact]
        public void NeverEnumeratesLazySequencesPastTheLimit()
        {
            var produced = 0;
            IEnumerable<int> Endless()
            {
                while (true)
                {
                    produced++;
                    yield return produced;
                }
            }
            Assert.EndsWith(",\"…[mais]\"]", Write(Endless()));
            Assert.Equal(51, produced);
        }

        [Fact]
        public void PreparesAttributesWithTheByteLimit()
        {
            Assert.Equal("{\"value\":5}", JsonWriter.Write(Attributes.Prepare(5, RedactMode.Mask)));
            var big = Attributes.Prepare(new { text = new string('x', 1999), more = Enumerable.Repeat(new string('y', 1999), 20).ToList() }, RedactMode.Mask);
            Assert.Equal(new[] { "_truncated" }, big.Keys.ToArray());
            Assert.True(Encoding.UTF8.GetByteCount(JsonWriter.Write(big)) < 16384 + 100);
        }
    
        [Fact]
        public void ReadsNewtonsoftTokensAsTheirValues()
        {
            var token = Newtonsoft.Json.Linq.JObject.Parse("{\"plan\":\"pro\",\"amount\":9.5,\"tags\":[\"a\",true,null],\"count\":3}");
            Assert.Equal("{\"payload\":{\"plan\":\"pro\",\"amount\":9.5,\"tags\":[\"a\",true,null],\"count\":3}}", JsonWriter.Write((JsonObject)Sanitizer.Sanitize(new { payload = token })!));
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void ReadsJsonDocumentsAndNodesAsTheirValues()
        {
            var element = System.Text.Json.JsonDocument.Parse("{\"plan\":\"pro\",\"amount\":9.9,\"tags\":[\"a\",true,null],\"count\":3}").RootElement;
            var node = System.Text.Json.Nodes.JsonNode.Parse("{\"plan\":\"pro\",\"amount\":9.9,\"ok\":false}");
            var record = (JsonObject)Sanitizer.Sanitize(new { element, node })!;
            Assert.Equal("{\"element\":{\"plan\":\"pro\",\"amount\":9.9,\"tags\":[\"a\",true,null],\"count\":3},\"node\":{\"plan\":\"pro\",\"amount\":9.9,\"ok\":false}}", JsonWriter.Write(record));
        }
#endif
}
}
