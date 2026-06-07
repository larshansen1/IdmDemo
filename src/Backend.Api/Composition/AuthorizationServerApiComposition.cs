using System.Threading.RateLimiting;
using Backend.Api.Services;
using Backend.Application.Extensions;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Composition;

public static class AuthorizationServerApiComposition
{
    public const string TokenEndpointRateLimitPolicy = "token-endpoint-per-ip";

    private static readonly PathString[] _publicRoutePrefixes =
    [
        new("/.well-known"),
        new("/connect/token"),
    ];

    public static AuthorizationServerOptions AddAuthorizationServerBoundary(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var authorizationServerOptions = new AuthorizationServerOptions
        {
            DpopSupportedAlgorithms = [],
        };
        builder.Configuration
            .GetSection("AuthorizationServer")
            .Bind(authorizationServerOptions);

        builder.Services.AddApplication(authorizationServerOptions);
        builder.Services.AddScoped<IAuthorizationServerService, AuthorizationServerService>();
        builder.Services.AddScoped<IDpopReplayCache, PersistentDpopReplayCache>();
        builder.Services.AddSingleton<IClientCertificateReader, ClientCertificateReader>();
        builder.Services.AddAuthorizationServerRateLimiting(builder.Configuration);

        return authorizationServerOptions;
    }

    public static WebApplication UseAuthorizationServerBoundary(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRateLimiter();
        return app;
    }

    public static ControllerActionEndpointConventionBuilder AddAuthorizationServerRouteOwnership(
        this ControllerActionEndpointConventionBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.Add(endpointBuilder =>
        {
            if (endpointBuilder is RouteEndpointBuilder routeEndpointBuilder &&
                IsAuthorizationServerRoute(new PathString($"/{routeEndpointBuilder.RoutePattern.RawText}")))
            {
                endpointBuilder.Metadata.Add(ApiBoundaryMetadata.AuthorizationServer);
            }
        });

        return endpoints;
    }

    public static bool IsAuthorizationServerRoute(PathString path)
    {
        foreach (var prefix in _publicRoutePrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IServiceCollection AddAuthorizationServerRateLimiting(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        var tokenRateLimitSection = configuration.GetSection("RateLimiting:TokenEndpoint");
        var tokenRateLimitPermitLimit = tokenRateLimitSection.GetValue("PermitLimit", 60);
        var tokenRateLimitWindowSeconds = tokenRateLimitSection.GetValue("WindowSeconds", 60);
        var tokenRateLimitSegmentsPerWindow = tokenRateLimitSection.GetValue("SegmentsPerWindow", 4);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(TokenEndpointRateLimitPolicy, httpContext =>
            {
                var remoteIpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    remoteIpAddress,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = tokenRateLimitPermitLimit,
                        Window = TimeSpan.FromSeconds(tokenRateLimitWindowSeconds),
                        SegmentsPerWindow = tokenRateLimitSegmentsPerWindow,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
