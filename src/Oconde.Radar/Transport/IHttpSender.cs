using System;
using System.Collections.Generic;

namespace Oconde.Radar.Transport
{
    internal interface IHttpSender
    {
        HttpOutcome Post(string url, byte[] body, bool gzip, IDictionary<string, string> headers, TimeSpan timeout);
    }
}
