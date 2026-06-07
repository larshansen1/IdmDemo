using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Mcp;
using Backend.Mcp.Api;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmApiTokenProviderTests : IDisposable
{
    private readonly string _certPath;

    public IdmApiTokenProviderTests()
    {
        this._certPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.pem");
        WriteTempCert(this._certPath);
    }

    public void Dispose()
    {
        if (File.Exists(this._certPath))
        {
            File.Delete(this._certPath);
        }
    }

    [Fact]
    public async Task GetBoundTokenAsync_FirstCall_AcquiresAndCachesToken()
    {
        var (provider, handler) = CreateProvider(this._certPath, "first-access-token", expiresIn: 300);

        var token = await provider.GetBoundTokenAsync("prod");

        Assert.Equal("first-access-token", token.AccessToken);
        Assert.NotNull(token.DpopKey);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetBoundTokenAsync_SecondCallWithinTtl_ReturnsCachedToken()
    {
        var (provider, handler) = CreateProvider(this._certPath, "cached-token", expiresIn: 300);

        var first = await provider.GetBoundTokenAsync("prod-cached");
        var second = await provider.GetBoundTokenAsync("prod-cached");

        Assert.Equal(first.AccessToken, second.AccessToken);
        Assert.Same(first.DpopKey, second.DpopKey);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetBoundTokenAsync_TokenRequest_IncludesDpopHeader()
    {
        var (provider, handler) = CreateProvider(this._certPath, "dpop-token", expiresIn: 300);

        await provider.GetBoundTokenAsync("prod-dpop");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("DPoP"));
    }

    [Fact]
    public async Task GetBoundTokenAsync_TokenRequest_IncludesClientCertHeader()
    {
        var (provider, handler) = CreateProvider(this._certPath, "cert-token", expiresIn: 300);

        await provider.GetBoundTokenAsync("prod-cert");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("X-Client-Cert"));
    }

    [Fact]
    public async Task GetBoundTokenAsync_TokenRequest_SendsGrantTypeAndClientId()
    {
        var (provider, handler) = CreateProvider(this._certPath, "grant-token", expiresIn: 300);

        await provider.GetBoundTokenAsync("prod-grant");

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("grant_type=client_credentials", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBoundTokenAsync_MissingCert_ThrowsMcpConfigurationException()
    {
        var (provider, _) = CreateProvider("/nonexistent/path/cert.pem", "unused", expiresIn: 300);

        await Assert.ThrowsAsync<McpConfigurationException>(() =>
            provider.GetBoundTokenAsync("prod-missing-cert"));
    }

    [Fact]
    public async Task GetBoundTokenAsync_TokenEndpointError_ThrowsHttpRequestException()
    {
        using var handler = new StatusCodeHandler(HttpStatusCode.Unauthorized);
        var factory = CreateFactory(handler);
        var provider = CreateProviderWith(this._certPath, factory);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetBoundTokenAsync("prod-err"));
    }

    private static (IdmApiTokenProvider Provider, CapturingTokenHandler Handler) CreateProvider(
        string certPath, string accessToken, int expiresIn)
    {
        var response = new TokenResponse { AccessToken = accessToken, ExpiresIn = expiresIn };
        var handler = new CapturingTokenHandler(response);
        var factory = CreateFactory(handler);
        return (CreateProviderWith(certPath, factory), handler);
    }

    private static IdmApiTokenProvider CreateProviderWith(string certPath, IHttpClientFactory factory)
    {
        var resolver = Substitute.For<IIdmApiInstanceResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(call =>
            new ResolvedIdmApiInstance(
                call.ArgAt<string?>(0) ?? "prod",
                new Uri("http://localhost:5000/"),
                "test-client",
                certPath));
        return new IdmApiTokenProvider(resolver, factory);
    }

    private static IHttpClientFactory CreateFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
#pragma warning disable CA2000
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler, false));
#pragma warning restore CA2000
        return factory;
    }

    private static void WriteTempCert(string path)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllText(path, cert.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem());
    }

    private sealed class CapturingTokenHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        private readonly TokenResponse _response;

        public CapturingTokenHandler(TokenResponse response) => this._response = response;

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.LastRequest = request;
            this.LastRequestBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;
            var json = JsonSerializer.Serialize(this._response, _jsonOptions);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StatusCodeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StatusCodeHandler(HttpStatusCode statusCode) => this._statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(this._statusCode));
        }
    }
}
