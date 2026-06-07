using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.As.Domain;
using Backend.As.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Services;

public sealed class AuthorizationServerServiceTests
{
    [Fact]
    public void GetDiscovery_ReturnsConfiguredMetadata()
    {
        var service = CreateService();

        var discovery = service.GetDiscovery();

        Assert.Equal("https://issuer.test", discovery.Issuer);
        Assert.Equal(new Uri("https://issuer.test/connect/token"), discovery.TokenEndpoint);
        Assert.Equal(new Uri("https://issuer.test/.well-known/jwks.json"), discovery.JwksUri);
        Assert.Contains("client_credentials", discovery.GrantTypesSupported);
        Assert.Contains("self_signed_tls_client_auth", discovery.TokenEndpointAuthMethodsSupported);
        Assert.True(discovery.TlsClientCertificateBoundAccessTokens);
        Assert.Equal(["ES256", "RS256"], discovery.DpopSigningAlgValuesSupported);
    }

    [Fact]
    public async Task GetJwksAsync_ReturnsPublicKey()
    {
        var service = CreateService();

        var jwks = await service.GetJwksAsync();

        var key = Assert.Single(jwks.Keys);
        Assert.Equal("test-key", key.KeyId);
        Assert.Equal("RSA", key.KeyType);
        Assert.Equal("sig", key.PublicKeyUse);
        Assert.Equal("RS256", key.Algorithm);
        Assert.NotEmpty(key.Modulus);
        Assert.NotEmpty(key.Exponent);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_ValidRequest_ReturnsJwt()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read", "orders.write"], ["service-admin"]);
        var service = CreateService(context: context);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            context.ClientId,
            "orders.read",
            certificate);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("orders.read", response.Scope);

        var header = ReadJwtHeader(response.AccessToken);
        Assert.Equal("at+jwt", header.GetProperty("typ").GetString());

        var payload = ReadJwtPayload(response.AccessToken);
        Assert.Equal("https://issuer.test", payload.GetProperty("iss").GetString());
        Assert.Equal(context.ClientRecordId.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal(context.ClientId, payload.GetProperty("client_id").GetString());
        Assert.Equal("api://default", payload.GetProperty("aud").GetString());
        Assert.Equal("orders.read", payload.GetProperty("scope").GetString());
        Assert.Equal("service-admin", payload.GetProperty("roles")[0].GetString());
        Assert.True(payload.TryGetProperty("cnf", out var confirmation));
        Assert.NotEmpty(confirmation.GetProperty("x5t#S256").GetString()!);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_ValidDpopProof_ReturnsDpopBoundJwt()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read"]);
        var dpopProofValidator = Substitute.For<IDpopProofValidator>();
        dpopProofValidator
            .ValidateTokenEndpointProofAsync(
                "proof.jwt",
                new Uri("https://issuer.test/connect/token"),
                Arg.Any<CancellationToken>())
            .Returns(new ValidatedDpopProof { JwkThumbprint = "test-jkt" });
        var service = CreateService(context: context, dpopProofValidator: dpopProofValidator);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            context.ClientId,
            "orders.read",
            certificate,
            "proof.jwt");

        Assert.Equal("DPoP", response.TokenType);
        var payload = ReadJwtPayload(response.AccessToken);
        var confirmation = payload.GetProperty("cnf");
        Assert.Equal("test-jkt", confirmation.GetProperty("jkt").GetString());
        Assert.False(confirmation.TryGetProperty("x5t#S256", out _));
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_McpResourceWithMcpScope_ReturnsMcpAudienceJwt()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["idm.mcp.read"]);
        var service = CreateService(context: context);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            context.ClientId,
            "idm.mcp.read",
            certificate,
            resource: "idm-demo-mcp");

        var payload = ReadJwtPayload(response.AccessToken);
        Assert.Equal("idm-demo-mcp", payload.GetProperty("aud").GetString());
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_McpResourceWithoutMcpScope_ThrowsInvalidTarget()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read"]);
        var service = CreateService(context: context);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync(
                "client_credentials",
                context.ClientId,
                "orders.read",
                certificate,
                resource: "idm-demo-mcp"));

        Assert.Equal("invalid_target", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnknownResource_ThrowsInvalidTarget()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["idm.mcp.read"]);
        var service = CreateService(context: context);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync(
                "client_credentials",
                context.ClientId,
                "idm.mcp.read",
                certificate,
                resource: "unknown-resource"));

        Assert.Equal("invalid_target", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_RequireDpopWithoutProof_ThrowsInvalidDpopProof()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate);
        var service = CreateService(
            context: context,
            options: new AuthorizationServerOptions
            {
                Issuer = "https://issuer.test",
                Audience = "api://default",
                AccessTokenLifetimeSeconds = 3600,
                RequireDpop = true,
            });

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", context.ClientId, null, certificate));

        Assert.Equal("invalid_dpop_proof", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_NoRequestedScope_GrantsAllAssignedScopes()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read", "orders.write"]);
        var service = CreateService(context: context);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            context.ClientId,
            null,
            certificate);

        Assert.Equal("orders.read orders.write", response.Scope);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_MissingGrantType_ThrowsInvalidRequest()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync(null, "orders-service", null, null));

        Assert.Equal("invalid_request", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnsupportedGrantType_ThrowsUnsupportedGrantType()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("password", "orders-service", null, null));

        Assert.Equal("unsupported_grant_type", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_MissingClientId_ThrowsInvalidRequest()
    {
        var service = CreateService(
            providerException: new OAuthException("invalid_request", "client_id is required.", 400));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", null, null, null));

        Assert.Equal("invalid_request", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_MissingCertificate_ThrowsInvalidClient()
    {
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client certificate is missing or invalid.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, null));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnknownClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client authentication failed.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_InactiveClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client authentication failed.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnregisteredCertificate_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client certificate is not registered.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_ThumbprintMismatch_ThrowsInvalidClient()
    {
        using var presentedCertificate = CreateCertificate("presented-service");
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client certificate does not match registration.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, presentedCertificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnassignedScope_ThrowsInvalidScope()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read"]);
        var service = CreateService(context: context);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", context.ClientId, "orders.write", certificate));

        Assert.Equal("invalid_scope", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_RegisteredCertificateRecord_ReturnsJwt()
    {
        using var certificate = CreateCertificate();
        var context = CreateContext(certificate, ["orders.read"]);
        var service = CreateService(context: context);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            context.ClientId,
            "orders.read",
            certificate);

        Assert.Equal("orders.read", response.Scope);
        Assert.NotEmpty(response.AccessToken);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_RevokedCertificateRecord_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var service = CreateService(
            providerException: new OAuthException("invalid_client", "Client certificate is revoked.", 401));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    private static AuthorizationServerService CreateService(
        IIssuanceContextProvider? issuanceContextProvider = null,
        IssuanceContext? context = null,
        OAuthException? providerException = null,
        IDpopProofValidator? dpopProofValidator = null,
        AuthorizationServerOptions? options = null)
    {
        issuanceContextProvider ??= Substitute.For<IIssuanceContextProvider>();
        if (providerException is not null)
        {
            issuanceContextProvider
                .ResolveAsync(Arg.Any<string?>(), Arg.Any<X509Certificate2?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IssuanceContext>(providerException));
        }
        else if (context is not null)
        {
            issuanceContextProvider
                .ResolveAsync(Arg.Any<string?>(), Arg.Any<X509Certificate2?>(), Arg.Any<CancellationToken>())
                .Returns(context);
        }

        return new AuthorizationServerService(
            options ?? new AuthorizationServerOptions
            {
                Issuer = "https://issuer.test",
                Audience = "api://default",
                AccessTokenLifetimeSeconds = 3600,
                RequireDpop = false,
            },
            issuanceContextProvider,
            new TestSigningKeyStore(),
            dpopProofValidator ?? Substitute.For<IDpopProofValidator>(),
            Substitute.For<ILogger<AuthorizationServerService>>());
    }

    private static IssuanceContext CreateContext(
        X509Certificate2 certificate,
        IReadOnlyList<string>? activeScopes = null,
        IReadOnlyList<string>? activeRoles = null)
    {
        return new IssuanceContext(
            Guid.NewGuid(),
            "orders-service",
            certificate,
            activeScopes ?? [],
            activeRoles ?? []);
    }

    private static X509Certificate2 CreateCertificate(string subjectName = "orders-service")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static JsonElement ReadJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadJwtHeader(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal);
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(base64);
    }

    private sealed class TestSigningKeyStore : IJwtSigningKeyStore
    {
        private readonly JwtSigningKey _key;

        public TestSigningKeyStore()
        {
            using var rsa = RSA.Create(2048);
            this._key = new JwtSigningKey
            {
                KeyId = "test-key",
                Parameters = rsa.ExportParameters(true),
            };
        }

        public Task<JwtSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this._key);
        }
    }
}
