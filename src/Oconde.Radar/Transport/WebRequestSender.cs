#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Oconde.Radar.Transport
{
    internal sealed class WebRequestSender : IHttpSender
    {
        public HttpOutcome Post(string url, byte[] body, bool gzip, IDictionary<string, string> headers, TimeSpan timeout)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.AllowAutoRedirect = false;
                request.KeepAlive = true;
                var milliseconds = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
                request.Timeout = milliseconds;
                request.ReadWriteTimeout = milliseconds;
                foreach (var header in headers)
                {
                    if (string.Equals(header.Key, "user-agent", StringComparison.OrdinalIgnoreCase)) request.UserAgent = header.Value;
                    else request.Headers[header.Key] = header.Value;
                }
                if (gzip) request.Headers["Content-Encoding"] = "gzip";
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse()) return Read(response);
            }
            catch (WebException error) when (error.Response is HttpWebResponse response)
            {
                using (response) return Read(response);
            }
            catch (Exception error)
            {
                return new HttpOutcome(0, null, error.Message);
            }
        }

        private static HttpOutcome Read(HttpWebResponse response)
        {
            try
            {
                using (var stream = response.GetResponseStream())
                {
                    if (stream != null)
                    {
                        var buffer = new byte[4096];
                        while (stream.Read(buffer, 0, buffer.Length) > 0)
                        {
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            return new HttpOutcome((int)response.StatusCode, HttpOutcome.ParseRetryAfter(response.Headers["Retry-After"]), null);
        }
    }
}
#endif
