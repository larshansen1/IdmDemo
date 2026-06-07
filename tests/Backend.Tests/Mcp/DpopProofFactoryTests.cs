using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Mcp.Api;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class DpopProofFactoryTests
{
    private static readonly Uri _tokenUri = new("http://localhost:5000/connect/token");
    private static readonly Uri _scimUri = new("http://localhost:5000/scim/v2/Clients");

    [Fact]
    public void Create_WithoutAccessToken_ProducesThreePartJwt()
    {
        using var key = RSA.Create(2048);

        var proof = DpopProofFactory.Create(key, "POST", _tokenUri);

        Assert.Equal(3, proof.Split('.').Length);
    }

    [Fact]
    public void Create_Header_ContainsCorrectTypeAndAlgorithm()
    {
        using var key = RSA.Create(2048);

        var proof = DpopProofFactory.Create(key, "POST", _tokenUri);
        var header = ParseJwtPart(proof.Split('.')[0]);

        Assert.Equal("dpop+jwt", header.GetProperty("typ").GetString());
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("RSA", header.GetProperty("jwk").GetProperty("kty").GetString());
    }

    [Fact]
    public void Create_Payload_ContainsRequiredClaims()
    {
        using var key = RSA.Create(2048);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var proof = DpopProofFactory.Create(key, "POST", _tokenUri);
        var payload = ParseJwtPart(proof.Split('.')[1]);

        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.Equal("POST", payload.GetProperty("htm").GetString());
        Assert.Equal("http://localhost:5000/connect/token", payload.GetProperty("htu").GetString());
        Assert.True(payload.GetProperty("iat").GetInt64() >= before);
        Assert.True(payload.GetProperty("iat").GetInt64() <= after);
        Assert.NotEmpty(payload.GetProperty("jti").GetString()!);
    }

    [Fact]
    public void Create_WithoutAccessToken_PayloadHasNoAthClaim()
    {
        using var key = RSA.Create(2048);

        var proof = DpopProofFactory.Create(key, "GET", _scimUri);
        var payload = ParseJwtPart(proof.Split('.')[1]);

        Assert.Equal(JsonValueKind.Undefined, payload.TryGetProperty("ath", out _) ? JsonValueKind.True : JsonValueKind.Undefined);
    }

    [Fact]
    public void Create_WithAccessToken_PayloadIncludesAthClaim()
    {
        using var key = RSA.Create(2048);
        const string token = "some.access.token";
        var expectedAth = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(token)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var proof = DpopProofFactory.Create(key, "GET", _scimUri, token);
        var payload = ParseJwtPart(proof.Split('.')[1]);

        Assert.Equal(expectedAth, payload.GetProperty("ath").GetString());
    }

    [Fact]
    public void Create_HttpMethodNormalisedToUpperCase()
    {
        using var key = RSA.Create(2048);

        var proof = DpopProofFactory.Create(key, "get", _scimUri);
        var payload = ParseJwtPart(proof.Split('.')[1]);

        Assert.Equal("GET", payload.GetProperty("htm").GetString());
    }

    [Fact]
    public void Create_UriWithQuery_HtuStripsQuery()
    {
        using var key = RSA.Create(2048);
        var uriWithQuery = new Uri("http://localhost:5000/scim/v2/Clients?filter=clientId+eq+%22foo%22");

        var proof = DpopProofFactory.Create(key, "GET", uriWithQuery);
        var payload = ParseJwtPart(proof.Split('.')[1]);

        Assert.Equal("http://localhost:5000/scim/v2/Clients", payload.GetProperty("htu").GetString());
    }

    [Fact]
    public void Create_DifferentCallsProduceDifferentJti()
    {
        using var key = RSA.Create(2048);

        var proof1 = DpopProofFactory.Create(key, "POST", _tokenUri);
        var proof2 = DpopProofFactory.Create(key, "POST", _tokenUri);

        var jti1 = ParseJwtPart(proof1.Split('.')[1]).GetProperty("jti").GetString();
        var jti2 = ParseJwtPart(proof2.Split('.')[1]).GetProperty("jti").GetString();
        Assert.NotEqual(jti1, jti2);
    }

    [Fact]
    public void Create_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DpopProofFactory.Create(null!, "POST", _tokenUri));
    }

    [Fact]
    public void Create_EmptyMethod_Throws()
    {
        using var key = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() =>
            DpopProofFactory.Create(key, string.Empty, _tokenUri));
    }

    [Fact]
    public void Create_NullUri_Throws()
    {
        using var key = RSA.Create(2048);
        Assert.Throws<ArgumentNullException>(() =>
            DpopProofFactory.Create(key, "POST", null!));
    }

    private static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var paddingNeeded = (4 - (base64.Length % 4)) % 4;
        base64 = base64.PadRight(base64.Length + paddingNeeded, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static JsonElement ParseJwtPart(string part) =>
        JsonDocument.Parse(DecodeBase64Url(part)).RootElement;
}
