using Backend.Api.Extensions;
using Backend.Api.Middleware;
using Backend.Api.Startup;

namespace Backend.Api.Composition;

public static class IdpAdminApiComposition
{
    public static WebApplicationBuilder AddIdpAdminApiBoundary(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddExceptionHandler<ScimExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHostedService<ScimAdminSeeder>();

        return builder;
    }

    public static WebApplication UseIdpAdminApiBoundary(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ScimOAuthMiddleware>();
        app.UseMiddleware<ScimAdminAuditMiddleware>();
        return app;
    }

    public static ControllerActionEndpointConventionBuilder AddIdpAdminApiRouteOwnership(
        this ControllerActionEndpointConventionBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.Add(endpointBuilder =>
        {
            if (endpointBuilder is RouteEndpointBuilder routeEndpointBuilder &&
                !AuthorizationServerApiComposition.IsAuthorizationServerRoute(
                    new PathString($"/{routeEndpointBuilder.RoutePattern.RawText}")))
            {
                endpointBuilder.Metadata.Add(ApiBoundaryMetadata.IdpAdminApi);
            }
        });

        return endpoints;
    }
}
