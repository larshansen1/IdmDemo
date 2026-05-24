using Microsoft.AspNetCore.Builder;

namespace Backend.Mcp;

public static class McpHostedApplicationBuilderExtensions
{
    public static IApplicationBuilder UseMcpHostedAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<McpHostedAuthenticationMiddleware>();
    }
}
