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
using Xunit;

namespace Backend.Tests.Services;

public sealed class DpopBoundAccessTokenValidatorTests
{
    private static readonly Uri _resourceUri = new("https://api.test/orders");

    [Fact]
    public async Task ValidateAsync_MatchingTokenAndProof_ReturnsValidatedToken()
    {
        using var clientCertificate = CreateCertificate();
        using var dpopKey = RSA.Create(2048);
        var fixture = CreateFixture();
        var accessToken = await IssueDpopTokenAsync(fixture, clientCertificate, dpopKey);
        var proof = CreateDpopProof(dpopKey, accessToken);

        var validated = await fixture.BoundValidator.ValidateAsync(accessToken, proof, "GET", _resourceUri);

        Assert.Equal("orders-service", validated.ClientId);
        Assert.Equal(ComputeJwkThumbprint(dpopKey), validated.DpopJwkThumbprint);
    }

    [Fact]
    public async Task ValidateAsync_AccessTokenHashMismatch_ThrowsInvalidDpopProof()
    {
        using var clientCertificate = CreateCertificate();
        using var dpopKey = RSA.Create(2048);
        var fixture = CreateFixture();
        var accessToken = await IssueDpopTokenAsync(fixture, clientCertificate, dpopKey);
        var proof = CreateDpopProof(dpopKey, "different-token");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.BoundValidator.ValidateAsync(accessToken, proof, "GET", _resourceUri));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateAsync_JwkThumbprintMismatch_ThrowsInvalidToken()
    {
        using var clientCertificate = CreateCertificate();
        using var tokenDpopKey = RSA.Create(2048);
        using var proofDpopKey = RSA.Create(2048);
        var fixture = CreateFixture();
        var accessToken = await IssueDpopTokenAsync(fixture, clientCertificate, tokenDpopKey);
        var proof = CreateDpopProof(proofDpopKey, accessToken);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            fixture.BoundValidator.ValidateAsync(accessToken, proof, "GET", _resourceUri));

        Assert.Equal("invalid_token", exception.Error);
    }

    private static TestFixture CreateFixture()
    {
        var options = new AuthorizationServerOptions
        {
            Issuer = "https://issuer.test",
            Audience = "api://default",
            AccessTokenLifetimeSeconds = 3600,
            DpopSupportedAlgorithms = ["RS256"],
        };
        var signingKeyStore = new TestSigningKeyStore();
        var dpopProofValidator = new DpopProofValidator(options, new InMemoryDpopReplayCache());
        var accessTokenValidator = new AccessTokenValidator(options, signingKeyStore);
        var boundValidator = new DpopBoundAccessTokenValidator(accessTokenValidator, dpopProofValidator);
        var clientRepository = Substitute.For<IMachineClientRepository>();
        var certificateRepository = Substitute.For<IMachineClientCertificateRepository>();
        var roleRepository = Substitute.For<IGlobalRoleRepository>();
        roleRepository.ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var scopeRepository = Substitute.For<IGlobalScopeRepository>();
        scopeRepository.ExistsActiveByValueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var authorizationServer = new AuthorizationServerService(
            options,
            clientRepository,
            certificateRepository,
            roleRepository,
            scopeRepository,
            signingKeyStore,
            dpopProofValidator,
            Substitute.For<ILogger<AuthorizationServerService>>());

        return new TestFixture(
            clientRepository,
            authorizationServer,
            boundValidator);
    }

    private static async Task<string> IssueDpopTokenAsync(TestFixture fixture, X509Certificate2 clientCertificate, RSA dpopKey)
    {
        var client = MachineClient.Create("orders-service", "Orders Service");
        client.AssignScopes(["orders.read"]);
        client.UpdateCertificate(
            Convert.ToHexString(SHA256.HashData(clientCertificate.RawData)),
            clientCertificate.Subject,
            clientCertificate.NotAfter);
        fixture.ClientRepository.GetByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);
        var tokenEndpointProof = CreateDpopProof(
            dpopKey,
            accessToken: null,
            httpMethod: "POST",
            uri: new Uri("https://issuer.test/connect/token"));

        var token = await fixture.AuthorizationServer.IssueClientCredentialsTokenAsync(
            "client_credentials",
            client.ClientId,
            "orders.read",
            clientCertificate,
            tokenEndpointProof);

        return token.AccessToken;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=orders-service",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string CreateDpopProof(
        RSA rsa,
        string? accessToken,
        string httpMethod = "GET",
        Uri? uri = null)
    {
        var parameters = rsa.ExportParameters(false);
        var jwk = new Dictionary<string, object?>
        {
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncode(parameters.Modulus ?? []),
            ["e"] = Base64UrlEncode(parameters.Exponent ?? []),
        };
        var header = new Dictionary<string, object?>
        {
            ["typ"] = "dpop+jwt",
            ["alg"] = "RS256",
            ["jwk"] = jwk,
        };
        var payload = new Dictionary<string, object?>
        {
            ["htm"] = httpMethod,
            ["htu"] = (uri ?? _resourceUri).ToString(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        if (accessToken is not null)
        {
            payload["ath"] = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        }

        var signingInput = string.Create(
            null,
            $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}");
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return string.Create(null, $"{signingInput}.{Base64UrlEncode(signature)}");
    }

    private static string ComputeJwkThumbprint(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var canonicalJson = string.Create(
            null,
            $"{{\"e\":\"{Base64UrlEncode(parameters.Exponent ?? [])}\",\"kty\":\"RSA\",\"n\":\"{Base64UrlEncode(parameters.Modulus ?? [])}\"}}");

        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private sealed record TestFixture(
        IMachineClientRepository ClientRepository,
        AuthorizationServerService AuthorizationServer,
        DpopBoundAccessTokenValidator BoundValidator);

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
