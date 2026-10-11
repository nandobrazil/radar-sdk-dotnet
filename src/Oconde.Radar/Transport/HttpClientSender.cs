#if NETSTANDARD
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace Oconde.Radar.Transport
{
    internal sealed class HttpClientSender : IHttpSender
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public HttpOutcome Post(string url, byte[] body, bool gzip, IDictionary<string, string> headers, TimeSpan timeout)
        {
            try
            {
                using (var cancellation = new CancellationTokenSource(timeout))
                using (var message = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    var content = new ByteArrayContent(body);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    if (gzip) content.Headers.ContentEncoding.Add("gzip");
                    message.Content = content;
                    foreach (var header in headers) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    using (var response = Client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellation.Token).ConfigureAwait(false).GetAwaiter().GetResult())
                    {
                        double? retryAfter = null;
                        var header = response.Headers.RetryAfter;
                        if (header?.Delta != null) retryAfter = Math.Min(Math.Max(0, header.Delta.Value.TotalSeconds), HttpOutcome.MaxRetryAfterSeconds);
                        else if (header?.Date != null) retryAfter = Math.Min(Math.Max(0, (header.Date.Value - DateTimeOffset.UtcNow).TotalSeconds), HttpOutcome.MaxRetryAfterSeconds);
                        return new HttpOutcome((int)response.StatusCode, retryAfter, null);
                    }
                }
            }
            catch (Exception error)
            {
                return new HttpOutcome(0, null, error.Message);
            }
        }
    }
}
#endif
