using Backend.Mcp.Api;
using Backend.Mcp.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdmMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<IdmApiInstancesOptions>()
            .BindConfiguration(IdmApiInstancesOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<McpRuntimeOptions>()
            .BindConfiguration(McpRuntimeOptions.SectionName)
            .ValidateOnStart();

        services.AddHttpClient<IIdmApiClient, IdmApiClient>();
        services.AddSingleton<IIdmApiInstanceResolver, IdmApiInstanceResolver>();
        services.AddSingleton<IMcpMutationGuard, McpMutationGuard>();
        services.AddSingleton<IMcpReadinessProbe, McpReadinessProbe>();
        services.AddSingleton<IValidateOptions<McpRuntimeOptions>, McpRuntimeOptionsValidator>();

        return services;
    }
}
