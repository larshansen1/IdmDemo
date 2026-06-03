using Backend.Mcp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmApiInstanceResolverTests
{
    [Fact]
    public void Resolve_NullInstance_ReturnsDefaultInstance()
    {
        var resolver = CreateResolver();

        var result = resolver.Resolve(null);

        Assert.Equal("local", result.Name);
        Assert.Equal(new Uri("https://localhost:5001"), result.BaseUrl);
    }

    [Fact]
    public void Resolve_ExplicitInstance_ReturnsSelectedInstance()
    {
        var resolver = CreateResolver();

        var result = resolver.Resolve("test");

        Assert.Equal("test", result.Name);
        Assert.Equal(new Uri("https://localhost:5003"), result.BaseUrl);
    }

    [Fact]
    public void Resolve_MissingInstance_ThrowsConfigurationException()
    {
        var resolver = CreateResolver();

        Assert.Throws<McpConfigurationException>(() => resolver.Resolve("missing"));
    }

    private static IdmApiInstanceResolver CreateResolver()
    {
        var instances = new IdmApiInstancesOptions
        {
            ["local"] = new IdmApiInstanceOptions
            {
                BaseUrl = new Uri("https://localhost:5001"),
                ClientId = "mcp-local",
                ClientCertificatePath = "/certs/local.pem",
            },
            ["test"] = new IdmApiInstanceOptions
            {
                BaseUrl = new Uri("https://localhost:5003"),
                ClientId = "mcp-test",
                ClientCertificatePath = "/certs/test.pem",
            },
        };
        var runtime = new McpRuntimeOptions { DefaultInstance = "local" };

        return new IdmApiInstanceResolver(Options.Create(instances), Options.Create(runtime));
    }
}
