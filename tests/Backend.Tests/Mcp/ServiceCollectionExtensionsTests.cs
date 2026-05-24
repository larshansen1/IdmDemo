using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.Domain.Services;
using Backend.Mcp;
using Backend.Mcp.Api;
using Backend.Mcp.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(nameof(McpProfile.LocalStdio), false, "stdio-audience")]
    [InlineData(nameof(McpProfile.LocalHostedDevelopment), false, "dev-audience")]
    [InlineData(nameof(McpProfile.HostedProduction), true, "prod-audience")]
    public void AddIdmMcp_RegistersCoreServicesForProfile(
        string profile,
        bool requireDpop,
        string audience)
    {
        var configuration = CreateConfiguration(profile, audience);
        using var provider = CreateProvider(configuration);

        Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>());
        Assert.NotNull(provider.GetRequiredService<IIdmApiClient>());
        Assert.NotNull(provider.GetRequiredService<IIdmApiInstanceResolver>());
        Assert.NotNull(provider.GetRequiredService<IMcpToolPolicyProvider>());
        Assert.NotNull(provider.GetRequiredService<IMcpMutationGuard>());
        Assert.NotNull(provider.GetRequiredService<IMcpReadinessProbe>());
        Assert.NotNull(provider.GetRequiredService<IJwtSigningKeyStore>());
        Assert.NotNull(provider.GetRequiredService<IDpopReplayCache>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAccessTokenValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDpopProofValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDpopBoundAccessTokenValidator>());

        var authOptions = provider.GetRequiredService<AuthorizationServerOptions>();
        Assert.Equal(audience, authOptions.Audience);
        Assert.Equal(requireDpop, authOptions.RequireDpop);
    }

    [Fact]
    public void AddIdmMcp_BindsRuntimeOptionsFromRegisteredConfiguration()
    {
        var configuration = CreateConfiguration(nameof(McpProfile.HostedProduction), "prod-audience");
        using var provider = CreateProvider(configuration);

        var runtimeOptions = provider.GetRequiredService<IOptions<McpRuntimeOptions>>().Value;

        Assert.Equal(McpProfile.HostedProduction, runtimeOptions.Profile);
        Assert.Equal("remote", runtimeOptions.DefaultInstance);
        Assert.Equal("prod-audience", runtimeOptions.Hosted.Audience);
    }

    [Fact]
    public void AddIdmMcp_ConfiguresAuthorizationServerOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Profile"] = nameof(McpProfile.HostedProduction),
                ["Mcp:DefaultInstance"] = "remote",
                ["Mcp:Hosted:Audience"] = "configured-audience",
                ["AuthorizationServer:Issuer"] = "https://issuer.example",
                ["AuthorizationServer:AccessTokenLifetimeSeconds"] = "7200",
                ["AuthorizationServer:DpopProofLifetimeSeconds"] = "60",
                ["AuthorizationServer:DpopReplayCacheSeconds"] = "90",
                ["AuthorizationServer:DpopSupportedAlgorithms:0"] = "ES256",
                ["AuthorizationServer:DpopSupportedAlgorithms:1"] = "PS256",
                ["IdmApiInstances:remote:BaseUrl"] = "https://idm.example",
                ["IdmApiInstances:remote:ApiKey"] = "secret",
            })
            .Build();
        using var provider = CreateProvider(configuration);

        var options = provider.GetRequiredService<AuthorizationServerOptions>();

        Assert.Equal("https://issuer.example", options.Issuer);
        Assert.Equal("configured-audience", options.Audience);
        Assert.Equal(7200, options.AccessTokenLifetimeSeconds);
        Assert.True(options.RequireDpop);
        Assert.Equal(60, options.DpopProofLifetimeSeconds);
        Assert.Equal(90, options.DpopReplayCacheSeconds);
        Assert.Equal(["ES256", "PS256"], options.DpopSupportedAlgorithms);
    }

    private static ServiceProvider CreateProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddIdmMcp(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IConfiguration CreateConfiguration(string profile, string audience)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Profile"] = profile,
                ["Mcp:DefaultInstance"] = "remote",
                ["Mcp:Hosted:Audience"] = audience,
                ["IdmApiInstances:remote:BaseUrl"] = "https://idm.example",
                ["IdmApiInstances:remote:ApiKey"] = "secret",
            })
            .Build();
    }
}
