using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Oconde.Radar.AspNetCore
{
    internal sealed class RadarStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMiddleware<RadarMiddleware>();
            next(app);
        };
    }
}
