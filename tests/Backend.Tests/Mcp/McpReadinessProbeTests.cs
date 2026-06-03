using System.Net;
using Backend.Mcp;
using Backend.Mcp.Health;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpReadinessProbeTests
{
    [Fact]
    public async Task CheckAsync_ReachableConfiguredInstance_ReturnsHealthy()
    {
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", report.Status);
        Assert.Equal(nameof(McpProfile.HostedProduction), report.Profile);
        Assert.Contains(report.Checks, check => check.Contains("local", StringComparison.Ordinal));
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task CheckAsync_MissingClientId_ReturnsUnhealthyWithoutCallingApi()
    {
        var called = false;
        var instances = CreateInstances(clientId: null);
        using var factory = new StubHttpClientFactory(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var probe = CreateProbe(factory, instances);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.False(called);
        Assert.Contains(report.Errors, error => error.Contains("missing ClientId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_HostedProductionReportsEffectiveAuthPosture()
    {
        var runtime = new McpRuntimeOptions
        {
            Profile = McpProfile.HostedProduction,
            Transport = McpTransport.Http,
        };
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory, runtime: runtime);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", report.Status);
        Assert.Equal(nameof(McpProfile.HostedProduction), report.Profile);
        Assert.Equal(nameof(McpTransport.Http), report.Transport);
        Assert.True(report.RequiresCallerAuthentication);
        Assert.True(report.RequireDpop);
        Assert.False(report.AllowBearerTokensForDevelopment);
        Assert.True(report.ReadOnly);
        Assert.Equal(nameof(McpProfile.HostedProduction), report.Effective.Profile);
        Assert.Equal(nameof(McpTransport.Http), report.Effective.Transport);
        Assert.True(report.Effective.RequiresCallerAuthentication);
        Assert.True(report.Effective.RequireDpop);
        Assert.False(report.Effective.AllowBearerTokensForDevelopment);
        Assert.True(report.Effective.ReadOnly);
        Assert.Contains(report.Checks, check => check.Contains("require DPoP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_DevelopmentBearerTokensAllowed_ReturnsHealthy()
    {
        var runtime = new McpRuntimeOptions
        {
            Profile = McpProfile.LocalHostedDevelopment,
            Transport = McpTransport.Http,
        };
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory, runtime: runtime);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", report.Status);
        Assert.Equal(nameof(McpProfile.LocalHostedDevelopment), report.Profile);
        Assert.True(report.AllowBearerTokensForDevelopment);
        Assert.Equal(nameof(McpProfile.LocalHostedDevelopment), report.Raw.Profile);
        Assert.Null(report.Raw.RequireDpop);
        Assert.Null(report.Raw.AllowBearerTokensForDevelopment);
        Assert.Equal(nameof(McpProfile.LocalHostedDevelopment), report.Effective.Profile);
        Assert.True(report.Effective.RequiresCallerAuthentication);
        Assert.False(report.Effective.RequireDpop);
        Assert.True(report.Effective.AllowBearerTokensForDevelopment);
        Assert.Contains(
            report.Checks,
            check => check.Contains("bearer tokens are enabled for development", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_LegacyHttpDevelopmentBearerReportsRawAndEffectivePosture()
    {
        var runtime = new McpRuntimeOptions
        {
            Transport = McpTransport.Http,
            ReadOnly = true,
            Hosted = new McpHostedOptions
            {
                AllowBearerTokensForDevelopment = true,
                Audience = "legacy-mcp",
            },
        };
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory, runtime: runtime);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", report.Status);
        Assert.Null(report.Raw.Profile);
        Assert.Equal(nameof(McpTransport.Http), report.Raw.Transport);
        Assert.Null(report.Raw.RequireDpop);
        Assert.True(report.Raw.AllowBearerTokensForDevelopment);
        Assert.Equal("legacy-mcp", report.Raw.Audience);
        Assert.True(report.Raw.ReadOnly);
        Assert.Equal(nameof(McpProfile.LocalHostedDevelopment), report.Effective.Profile);
        Assert.Equal(nameof(McpTransport.Http), report.Effective.Transport);
        Assert.True(report.Effective.RequiresCallerAuthentication);
        Assert.False(report.Effective.RequireDpop);
        Assert.True(report.Effective.AllowBearerTokensForDevelopment);
        Assert.Equal("legacy-mcp", report.Effective.Audience);
        Assert.True(report.Effective.ReadOnly);
    }

    [Fact]
    public async Task CheckAsync_MissingHostedAudience_ReturnsUnhealthy()
    {
        var runtime = new McpRuntimeOptions
        {
            Profile = McpProfile.HostedProduction,
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions
            {
                Audience = string.Empty,
            },
        };
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory, runtime: runtime);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.Contains(report.Errors, error => error.Contains("Audience is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_NoConfiguredInstances_ReturnsUnhealthy()
    {
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(factory, new IdmApiInstancesOptions());

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.Contains(report.Errors, error => error.Contains("At least one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_MissingBaseUrl_ReturnsUnhealthyWithoutCallingApi()
    {
        var called = false;
        var instances = new IdmApiInstancesOptions
        {
            ["local"] = new IdmApiInstanceOptions
            {
                ClientId = "mcp-local",
                ClientCertificatePath = "/certs/local.pem",
            },
        };
        using var factory = new StubHttpClientFactory(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var probe = CreateProbe(factory, instances);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.False(called);
        Assert.Contains(report.Errors, error => error.Contains("missing BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_ApiReturnsFailureStatus_ReturnsUnhealthy()
    {
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var probe = CreateProbe(factory);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.Contains(report.Errors, error => error.Contains("401 Unauthorized", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_ApiThrowsHttpRequestException_ReturnsUnhealthy()
    {
        using var factory = new StubHttpClientFactory(_ => throw new HttpRequestException("connection failed"));
        var probe = CreateProbe(factory);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.Contains(report.Errors, error => error.Contains("connection failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_ApiTimesOut_ReturnsUnhealthy()
    {
        using var factory = new StubHttpClientFactory(_ => throw new TaskCanceledException("request timed out"));
        var probe = CreateProbe(factory);

        var report = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", report.Status);
        Assert.Contains(report.Errors, error => error.Contains("timed out", StringComparison.Ordinal));
    }

    private static McpReadinessProbe CreateProbe(
        IHttpClientFactory httpClientFactory,
        IdmApiInstancesOptions? instances = null,
        McpRuntimeOptions? runtime = null)
    {
        return new McpReadinessProbe(
            httpClientFactory,
            Options.Create(instances ?? CreateInstances()),
            Options.Create(runtime ?? new McpRuntimeOptions { Profile = McpProfile.HostedProduction }));
    }

    private static IdmApiInstancesOptions CreateInstances(string? clientId = "mcp-local", string? certPath = "/certs/local.pem")
    {
        return new IdmApiInstancesOptions
        {
            ["local"] = new IdmApiInstanceOptions
            {
                BaseUrl = new Uri("http://127.0.0.1:5000"),
                ClientId = clientId,
                ClientCertificatePath = certPath,
            },
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly StubHttpMessageHandler _handler;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this._handler = new StubHttpMessageHandler(handler);
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(this._handler, disposeHandler: false);
        }

        public void Dispose()
        {
            this._handler.Dispose();
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this._handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(this._handler(request));
        }
    }
}
