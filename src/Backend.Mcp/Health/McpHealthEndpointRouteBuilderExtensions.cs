using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Mcp.Health;

public static class McpHealthEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapIdmMcpHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            "/health/live",
            () => Results.Ok(new
            {
                status = "Healthy",
                service = "Backend.Mcp",
            }));

        endpoints.MapGet(
            "/health/ready",
            async (IMcpReadinessProbe probe, CancellationToken cancellationToken) =>
            {
                var report = await probe.CheckAsync(cancellationToken).ConfigureAwait(false);
                return report.Status == "Healthy"
                    ? Results.Ok(report)
                    : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
            });

        return endpoints;
    }
}
