using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.Domain.Services;
using Backend.Infrastructure.Signing;
using Backend.Mcp.Api;
using Backend.Mcp.Audit;
using Backend.Mcp.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdmMcp(this IServiceCollection services)
    {
        return AddIdmMcp(services, null);
    }

    public static IServiceCollection AddIdmMcp(this IServiceCollection services, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<IdmApiInstancesOptions>()
            .BindConfiguration(IdmApiInstancesOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<McpRuntimeOptions>()
            .BindConfiguration(McpRuntimeOptions.SectionName)
            .ValidateOnStart();

        var runtimeOptions = configuration?
            .GetSection(McpRuntimeOptions.SectionName)
            .Get<McpRuntimeOptions>() ?? new McpRuntimeOptions();
        var effectiveSettings = McpRuntimeProfileResolver.Resolve(runtimeOptions);
        var authorizationServerOptions = CreateAuthorizationServerOptions(configuration, effectiveSettings);
        var signingKeyPath = configuration?["AuthorizationServer:SigningKeyPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "signing-key.json");

        services.AddHttpContextAccessor();
        services.AddHttpClient<IIdmApiClient, IdmApiClient>();
        services.AddHttpClient("idm-token");
        services.AddSingleton<IIdmApiTokenProvider, IdmApiTokenProvider>();
        services.AddSingleton(authorizationServerOptions);
        services.AddSingleton<IJwtSigningKeyStore>(_ => new LocalJwtSigningKeyStore(signingKeyPath));
        services.AddSingleton<IDpopReplayCache, InMemoryDpopReplayCache>();
        services.AddScoped<IDpopProofValidator, DpopProofValidator>();
        services.AddScoped<IAccessTokenValidator, AccessTokenValidator>();
        services.AddScoped<IDpopBoundAccessTokenValidator, DpopBoundAccessTokenValidator>();
        services.AddSingleton<IIdmApiInstanceResolver, IdmApiInstanceResolver>();
        services.AddSingleton<IMcpToolPolicyProvider, McpToolPolicyProvider>();
        services.AddSingleton<IMcpMutationGuard, McpMutationGuard>();
        services.AddSingleton<McpToolAuditContextFactory>();
        services.AddSingleton<McpToolCallFilter>();
        services.AddSingleton<IMcpToolAuditLogger, McpToolAuditLogger>();
        services.AddSingleton<IMcpReadinessProbe, McpReadinessProbe>();
        services.AddSingleton<IValidateOptions<McpRuntimeOptions>, McpRuntimeOptionsValidator>();

        return services;
    }

    private static AuthorizationServerOptions CreateAuthorizationServerOptions(
        IConfiguration? configuration,
        McpEffectiveRuntimeSettings runtimeSettings)
    {
        var section = configuration?.GetSection("AuthorizationServer");
        var supportedAlgorithms = section?.GetSection("DpopSupportedAlgorithms").Get<string[]>()
            ?? ["ES256", "RS256"];

        return new AuthorizationServerOptions
        {
            Issuer = configuration?["AuthorizationServer:Issuer"] ?? "https://localhost:5001",
            Audience = runtimeSettings.Audience,
            AccessTokenLifetimeSeconds = ReadInt(configuration, "AuthorizationServer:AccessTokenLifetimeSeconds", 3600),
            RequireDpop = runtimeSettings.RequireDpop,
            DpopProofLifetimeSeconds = ReadInt(configuration, "AuthorizationServer:DpopProofLifetimeSeconds", 300),
            DpopReplayCacheSeconds = ReadInt(configuration, "AuthorizationServer:DpopReplayCacheSeconds", 300),
            DpopSupportedAlgorithms = supportedAlgorithms,
        };
    }

    private static int ReadInt(IConfiguration? configuration, string key, int defaultValue)
    {
        var value = configuration?[key];
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
