using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Oconde.Radar.Json;
using Oconde.Radar.Redaction;
using Xunit;

namespace Oconde.Radar.Tests
{
    public class RedactionParityTests
    {
        private static readonly JsonElement Vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "redaction-vectors.json"))).RootElement;

        public RedactionParityTests()
        {
            Redactor.SetFingerprintSecret(Vectors.GetProperty("secret").GetString());
        }

        private static object? Tree(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    return element.TryGetInt64(out var integer) ? integer : (object)element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Object:
                    var record = new JsonObject();
                    foreach (var property in element.EnumerateObject()) record.Set(property.Name, Tree(property.Value));
                    return record;
                case JsonValueKind.Array:
                    var items = new JsonArray();
                    items.AddRange(element.EnumerateArray().Select(Tree));
                    return items;
                default:
                    return null;
            }
        }

        [Fact]
        public void ClassifiesKeysHeadersAndCredentialsLikeTheNodeSdk()
        {
            foreach (var vector in Vectors.GetProperty("keys").EnumerateArray())
            {
                var key = vector.GetProperty("key").GetString()!;
                Assert.True(vector.GetProperty("sensitive").GetBoolean() == Redactor.IsSensitiveKey(key), "sensitive " + key);
                Assert.True(vector.GetProperty("lowEntropy").GetBoolean() == Redactor.IsLowEntropyKey(key), "low entropy " + key);
            }
            foreach (var vector in Vectors.GetProperty("headers").EnumerateArray())
            {
                var name = vector.GetProperty("name").GetString()!;
                Assert.True(vector.GetProperty("sensitive").GetBoolean() == Redactor.IsSensitiveHeader(name), "header " + name);
            }
            foreach (var vector in Vectors.GetProperty("credentials").EnumerateArray())
            {
                var value = vector.GetProperty("value").GetString()!;
                Assert.True(vector.GetProperty("credential").GetBoolean() == Redactor.LooksLikeCredential(value), "credential " + value);
            }
        }

        [Fact]
        public void MasksValuesWithTheSameEdgesAndFingerprintAsTheNodeSdk()
        {
            foreach (var vector in Vectors.GetProperty("masks").EnumerateArray())
            {
                var masked = Redactor.MaskValue(Tree(vector.GetProperty("value")), vector.GetProperty("reveal").GetBoolean());
                Assert.Equal(vector.GetProperty("masked").GetString(), masked);
            }
        }

        [Fact]
        public void RedactsNestedDataUrlsAndPathsLikeTheNodeSdk()
        {
            var deep = Vectors.GetProperty("deep");
            Assert.Equal(JsonWriter.Write(Tree(deep.GetProperty("output"))), JsonWriter.Write(Redactor.RedactDeep(Tree(deep.GetProperty("input")), RedactMode.Mask)));
            Assert.Equal(JsonWriter.Write(Tree(deep.GetProperty("input"))), JsonWriter.Write(Redactor.RedactDeep(Tree(deep.GetProperty("input")), RedactMode.None)));
            foreach (var vector in Vectors.GetProperty("urls").EnumerateArray())
                Assert.Equal(vector.GetProperty("masked").GetString(), Redactor.RedactUrl(vector.GetProperty("url").GetString()!, RedactMode.Mask));
            foreach (var vector in Vectors.GetProperty("paths").EnumerateArray())
            {
                var parameters = vector.GetProperty("params").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!);
                Assert.Equal(vector.GetProperty("masked").GetString(), Redactor.RedactPath(vector.GetProperty("path").GetString()!, parameters, RedactMode.Mask));
            }
        }
    }
}
