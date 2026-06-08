using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Backend.IntegrationTests.Infrastructure;

internal sealed class SetLoopbackRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                await nextMiddleware(ctx);
            });
            next(app);
        };
    }
}
