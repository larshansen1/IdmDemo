using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.Domain.Entities;
using Backend.Domain.Repositories;
using Backend.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
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
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read", "orders.write"]);
        client.AssignRoles(["service-admin"]);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            client.ClientId,
            "orders.read",
            certificate);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("orders.read", response.Scope);

        var payload = ReadJwtPayload(response.AccessToken);
        Assert.Equal("https://issuer.test", payload.GetProperty("iss").GetString());
        Assert.Equal(client.Id.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal(client.ClientId, payload.GetProperty("client_id").GetString());
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
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read"]);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var dpopProofValidator = Substitute.For<IDpopProofValidator>();
        dpopProofValidator
            .ValidateTokenEndpointProofAsync(
                "proof.jwt",
                new Uri("https://issuer.test/connect/token"),
                Arg.Any<CancellationToken>())
            .Returns(new ValidatedDpopProof { JwkThumbprint = "test-jkt" });
        var service = CreateService(repository, dpopProofValidator: dpopProofValidator);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            client.ClientId,
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
    public async Task IssueClientCredentialsTokenAsync_RequireDpopWithoutProof_ThrowsInvalidDpopProof()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(
            repository,
            options: new AuthorizationServerOptions
            {
                Issuer = "https://issuer.test",
                Audience = "api://default",
                AccessTokenLifetimeSeconds = 3600,
                RequireDpop = true,
            });

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, null, certificate));

        Assert.Equal("invalid_dpop_proof", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_NoRequestedScope_GrantsAllAssignedScopes()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read", "orders.write"]);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            client.ClientId,
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
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", null, null, null));

        Assert.Equal("invalid_request", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_MissingCertificate_ThrowsInvalidClient()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, null));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnknownClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync("orders-service", Arg.Any<CancellationToken>()).ReturnsNull();
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", "orders-service", null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_InactiveClient_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.Deactivate();
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnregisteredCertificate_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var client = MachineClient.Create("orders-service", null);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_ThumbprintMismatch_ThrowsInvalidClient()
    {
        using var registeredCertificate = CreateCertificate();
        using var presentedCertificate = CreateCertificate("presented-service");
        var client = CreateClient(registeredCertificate);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, null, presentedCertificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_UnassignedScope_ThrowsInvalidScope()
    {
        using var certificate = CreateCertificate();
        var client = CreateClient(certificate);
        client.AssignScopes(["orders.read"]);
        var repository = Substitute.For<IMachineClientRepository>();
        repository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, "orders.write", certificate));

        Assert.Equal("invalid_scope", exception.Error);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_RegisteredCertificateRecord_ReturnsJwt()
    {
        using var certificate = CreateCertificate();
        var client = MachineClient.Create("orders-service", "Orders Service");
        client.AssignScopes(["orders.read"]);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository
            .GetByThumbprintAsync(client.Id, ComputeThumbprint(certificate), Arg.Any<CancellationToken>())
            .Returns(CreateCertificateRecord(client, certificate));
        var service = CreateService(clientRepository, certificateRepository);

        var response = await service.IssueClientCredentialsTokenAsync(
            "client_credentials",
            client.ClientId,
            "orders.read",
            certificate);

        Assert.Equal("orders.read", response.Scope);
        Assert.NotEmpty(response.AccessToken);
    }

    [Fact]
    public async Task IssueClientCredentialsTokenAsync_RevokedCertificateRecord_ThrowsInvalidClient()
    {
        using var certificate = CreateCertificate();
        var client = MachineClient.Create("orders-service", "Orders Service");
        var certificateRecord = CreateCertificateRecord(client, certificate);
        certificateRecord.Revoke("rotated");
        var clientRepository = Substitute.For<IMachineClientRepository>();
        clientRepository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        certificateRepository
            .GetByThumbprintAsync(client.Id, ComputeThumbprint(certificate), Arg.Any<CancellationToken>())
            .Returns(certificateRecord);
        var service = CreateService(clientRepository, certificateRepository);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            service.IssueClientCredentialsTokenAsync("client_credentials", client.ClientId, null, certificate));

        Assert.Equal("invalid_client", exception.Error);
        Assert.Equal(401, exception.StatusCode);
    }

    private static AuthorizationServerService CreateService(
        IMachineClientRepository? repository = null,
        IMachineClientCertificateRepository? certificateRepository = null,
        IDpopProofValidator? dpopProofValidator = null,
        AuthorizationServerOptions? options = null)
    {
        return new AuthorizationServerService(
            options ?? new AuthorizationServerOptions
            {
                Issuer = "https://issuer.test",
                Audience = "api://default",
                AccessTokenLifetimeSeconds = 3600,
            },
            repository ?? Substitute.For<IMachineClientRepository>(),
            certificateRepository ?? Substitute.For<IMachineClientCertificateRepository>(),
            new TestSigningKeyStore(),
            dpopProofValidator ?? Substitute.For<IDpopProofValidator>(),
            Substitute.For<ILogger<AuthorizationServerService>>());
    }

    private static MachineClient CreateClient(X509Certificate2 certificate)
    {
        var client = MachineClient.Create("orders-service", "Orders Service");
        client.UpdateCertificate(Convert.ToHexString(SHA256.HashData(certificate.RawData)), certificate.Subject, certificate.NotAfter);
        return client;
    }

    private static MachineClientCertificate CreateCertificateRecord(MachineClient client, X509Certificate2 certificate)
    {
        return MachineClientCertificate.Create(
            client.Id,
            "test certificate",
            ComputeThumbprint(certificate),
            certificate.Subject,
            certificate.Issuer,
            certificate.SerialNumber,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.ExportCertificatePem());
    }

    private static string ComputeThumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
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
