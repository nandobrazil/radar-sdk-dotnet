using Oconde.Radar.AspNetCore;

namespace Microsoft.AspNetCore.Builder
{
    public static class RadarApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseRadar(this IApplicationBuilder app) => app.UseMiddleware<RadarMiddleware>();
    }
}
