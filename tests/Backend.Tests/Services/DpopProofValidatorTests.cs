using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Xunit;

namespace Backend.Tests.Services;

public sealed class DpopProofValidatorTests
{
    private static readonly Uri _tokenEndpoint = new("https://issuer.test/connect/token");

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_ValidProof_ReturnsJwkThumbprint()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa);
        var validator = CreateValidator();

        var validated = await validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint);

        Assert.Equal(ComputeJwkThumbprint(rsa), validated.JwkThumbprint);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_ReplayedProof_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa);
        var validator = CreateValidator();
        await validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_WrongHttpMethod_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, httpMethod: "GET");
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_WrongHttpUri_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, httpUri: new Uri("https://issuer.test/other"));
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_ExpiredIssuedAt_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_FutureIssuedAt_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, issuedAt: DateTimeOffset.UtcNow.AddMinutes(3));
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_UnsupportedAlgorithm_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, algorithm: "HS256");
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_MissingJwk_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, includeJwk: false);
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_InvalidSignature_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, tamperPayloadAfterSigning: true);
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    [Fact]
    public async Task ValidateTokenEndpointProofAsync_PrivateJwkMaterial_ThrowsInvalidDpopProof()
    {
        using var rsa = RSA.Create(2048);
        var proof = CreateProof(rsa, includePrivateKey: true);
        var validator = CreateValidator();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            validator.ValidateTokenEndpointProofAsync(proof, _tokenEndpoint));

        Assert.Equal("invalid_dpop_proof", exception.Error);
    }

    private static DpopProofValidator CreateValidator()
    {
        return new DpopProofValidator(
            new AuthorizationServerOptions
            {
                DpopSupportedAlgorithms = ["RS256"],
                DpopProofLifetimeSeconds = 300,
                DpopReplayCacheSeconds = 300,
            },
            new InMemoryDpopReplayCache());
    }

    private static string CreateProof(
        RSA rsa,
        string httpMethod = "POST",
        Uri? httpUri = null,
        DateTimeOffset? issuedAt = null,
        string algorithm = "RS256",
        bool includeJwk = true,
        bool includePrivateKey = false,
        bool tamperPayloadAfterSigning = false)
    {
        var parameters = rsa.ExportParameters(includePrivateKey);
        var jwk = new Dictionary<string, object?>
        {
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncode(parameters.Modulus ?? []),
            ["e"] = Base64UrlEncode(parameters.Exponent ?? []),
        };

        if (includePrivateKey)
        {
            jwk["d"] = Base64UrlEncode(parameters.D ?? []);
        }

        var header = new Dictionary<string, object?>
        {
            ["typ"] = "dpop+jwt",
            ["alg"] = algorithm,
        };

        if (includeJwk)
        {
            header["jwk"] = jwk;
        }

        var payload = new Dictionary<string, object?>
        {
            ["htm"] = httpMethod,
            ["htu"] = (httpUri ?? _tokenEndpoint).ToString(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = string.Create(
            null,
            $"{encodedHeader}.{encodedPayload}");
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        if (tamperPayloadAfterSigning)
        {
            payload["jti"] = Guid.NewGuid().ToString();
            encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            signingInput = string.Create(null, $"{encodedHeader}.{encodedPayload}");
        }

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
}
