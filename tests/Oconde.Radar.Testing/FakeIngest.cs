using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Oconde.Radar.Testing
{
    public sealed class ReceivedRequest
    {
        public ReceivedRequest(string method, string path, IDictionary<string, string> headers, string body, bool compressed)
        {
            Method = method;
            Path = path;
            Headers = headers;
            Body = body;
            Compressed = compressed;
        }

        public string Method { get; }
        public string Path { get; }
        public IDictionary<string, string> Headers { get; }
        public string Body { get; }
        public bool Compressed { get; }

        public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();
    }

    public sealed class FakeIngest : IDisposable
    {
        private sealed class Reply
        {
            public Reply(int status, IDictionary<string, string> headers)
            {
                Status = status;
                Headers = headers;
            }

            public int Status { get; }
            public IDictionary<string, string> Headers { get; }
        }

        private readonly TcpListener listener;
        private readonly object gate = new object();
        private readonly List<ReceivedRequest> requests = new List<ReceivedRequest>();
        private readonly Queue<Reply> replies = new Queue<Reply>();
        private volatile bool stopped;

        public FakeIngest()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            new Thread(Serve) { IsBackground = true, Name = "fake-ingest" }.Start();
        }

        public string Url { get; }

        public TimeSpan Delay { get; set; }

        public IReadOnlyList<ReceivedRequest> Requests
        {
            get
            {
                lock (gate) return requests.ToList();
            }
        }

        public void Enqueue(int status, IDictionary<string, string>? headers = null)
        {
            lock (gate) replies.Enqueue(new Reply(status, headers ?? new Dictionary<string, string>()));
        }

        public bool WaitFor(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (gate)
            {
                while (requests.Count < count)
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return false;
                    Monitor.Wait(gate, left);
                }
                return true;
            }
        }

        public IReadOnlyList<JsonElement> Batches() => Requests.Where(request => request.Path == "/api/v1/events").Select(request => request.Json).ToList();

        public IReadOnlyList<JsonElement> Events() => Batches().SelectMany(batch => batch.GetProperty("events").EnumerateArray()).ToList();

        public IReadOnlyList<ReceivedRequest> CheckIns() => Requests.Where(request => request.Path.StartsWith("/api/v1/checkins/", StringComparison.Ordinal)).ToList();

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
        }

        private void Serve()
        {
            while (!stopped)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    var head = ReadHead(stream);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var requestLine = lines[0].Split(' ');
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in lines.Skip(1))
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }
                    var length = headers.TryGetValue("content-length", out var value) ? int.Parse(value) : 0;
                    var raw = ReadExactly(stream, length);
                    var compressed = headers.TryGetValue("content-encoding", out var encoding) && encoding == "gzip";
                    var body = Encoding.UTF8.GetString(compressed ? Decompress(raw) : raw);
                    if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);
                    Reply reply;
                    lock (gate)
                    {
                        reply = replies.Count > 0 ? replies.Dequeue() : new Reply(202, new Dictionary<string, string>());
                        requests.Add(new ReceivedRequest(requestLine[0], requestLine[1], headers, body, compressed));
                        Monitor.PulseAll(gate);
                    }
                    var response = new StringBuilder();
                    response.Append("HTTP/1.1 ").Append(reply.Status).Append(" Status\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n");
                    foreach (var header in reply.Headers) response.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                    response.Append("\r\n{}");
                    var bytes = Encoding.ASCII.GetBytes(response.ToString());
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch (IOException)
                {
                }
            }
        }

        private static string? ReadHead(NetworkStream stream)
        {
            var buffer = new List<byte>();
            while (true)
            {
                var next = stream.ReadByte();
                if (next < 0) return null;
                buffer.Add((byte)next);
                var count = buffer.Count;
                if (count >= 4 && buffer[count - 4] == '\r' && buffer[count - 3] == '\n' && buffer[count - 2] == '\r' && buffer[count - 1] == '\n')
                    return Encoding.ASCII.GetString(buffer.ToArray(), 0, count - 4);
            }
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            var data = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = stream.Read(data, offset, length - offset);
                if (read <= 0) break;
                offset += read;
            }
            return data;
        }

        private static byte[] Decompress(byte[] data)
        {
            using (var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
