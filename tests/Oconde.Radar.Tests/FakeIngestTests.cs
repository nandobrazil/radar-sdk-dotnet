using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using Oconde.Radar.Testing;
using Xunit;

namespace Oconde.Radar.Tests
{
    public class FakeIngestTests
    {
        [Fact]
        public void ReceivesAGzippedPostAndAnswersWithTheQueuedStatus()
        {
            using (var server = new FakeIngest())
            {
                server.Enqueue(429, new System.Collections.Generic.Dictionary<string, string> { ["Retry-After"] = "3" });
                Assert.Equal(429, Post(server.Url + "/api/v1/events", "{\"events\":[{\"type\":\"log\"}]}", out var retryAfter));
                Assert.Equal("3", retryAfter);
                Assert.Equal(202, Post(server.Url + "/api/v1/events", "{\"events\":[]}", out _));
                Assert.True(server.WaitFor(2, TimeSpan.FromSeconds(5)));
                Assert.True(server.Requests[0].Compressed);
                Assert.Single(server.Events());
            }
        }

        private static int Post(string url, string json, out string? retryAfter)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Content-Encoding"] = "gzip";
            var body = Gzip(Encoding.UTF8.GetBytes(json));
            request.ContentLength = body.Length;
            using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    retryAfter = response.Headers["Retry-After"];
                    return (int)response.StatusCode;
                }
            }
            catch (WebException error) when (error.Response is HttpWebResponse response)
            {
                retryAfter = response.Headers["Retry-After"];
                return (int)response.StatusCode;
            }
        }

        private static byte[] Gzip(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress)) gzip.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }
    }
}
