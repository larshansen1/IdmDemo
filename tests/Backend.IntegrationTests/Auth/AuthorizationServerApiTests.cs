using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Clients;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.Auth;

public sealed class AuthorizationServerApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AuthorizationServerApiTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._factory = factory;
        this._client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOpenIdConfiguration_ReturnsAuthorizationServerMetadata()
    {
        var response = await this._client.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await response.Content.ReadFromJsonAsync<DiscoveryResponse>(_jsonOptions);
        Assert.NotNull(metadata);
        Assert.Equal("https://idmdemo.test", metadata.Issuer);
        Assert.Equal(new Uri("https://idmdemo.test/connect/token"), metadata.TokenEndpoint);
        Assert.Equal(new Uri("https://idmdemo.test/.well-known/jwks.json"), metadata.JwksUri);
        Assert.Contains("client_credentials", metadata.GrantTypesSupported);
        Assert.Contains("self_signed_tls_client_auth", metadata.TokenEndpointAuthMethodsSupported);
        Assert.True(metadata.TlsClientCertificateBoundAccessTokens);
    }

    [Fact]
    public async Task GetJwks_ReturnsPublicSigningKey()
    {
        var response = await this._client.GetAsync(new Uri("/.well-known/jwks.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwks = await response.Content.ReadFromJsonAsync<JwksResponse>(_jsonOptions);
        Assert.NotNull(jwks);
        var key = Assert.Single(jwks.Keys);
        Assert.Equal("RSA", key.KeyType);
        Assert.Equal("sig", key.PublicKeyUse);
        Assert.Equal("RS256", key.Algorithm);
        Assert.NotEmpty(key.KeyId);
        Assert.NotEmpty(key.Modulus);
        Assert.NotEmpty(key.Exponent);
    }

    [Fact]
    public async Task PostToken_ValidClientCredentials_ReturnsCertificateBoundJwt()
    {
        var clientId = $"orders-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        var client = await this.CreateClientAsync(
            clientId,
            ComputeThumbprintHex(certificate),
            ["orders.read", "orders.write"],
            ["service-admin"]);
        using var request = CreateTokenRequest(client.ClientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));

        var response = await this._client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        Assert.Equal("Bearer", tokenResponse.TokenType);
        Assert.Equal(3600, tokenResponse.ExpiresIn);
        Assert.Equal("orders.read", tokenResponse.Scope);

        var payload = ReadJwtPayload(tokenResponse.AccessToken);
        Assert.Equal("https://idmdemo.test", payload.GetProperty("iss").GetString());
        Assert.Equal(client.Id, payload.GetProperty("sub").GetString());
        Assert.Equal(clientId, payload.GetProperty("client_id").GetString());
        Assert.Equal("idm-demo-api", payload.GetProperty("aud").GetString());
        Assert.Equal("orders.read", payload.GetProperty("scope").GetString());
        Assert.Equal("service-admin", payload.GetProperty("roles")[0].GetString());
        Assert.Equal(
            ComputeThumbprintBase64Url(certificate),
            payload.GetProperty("cnf").GetProperty("x5t#S256").GetString());
    }

    [Fact]
    public async Task PostToken_MissingCertificate_ReturnsInvalidClient()
    {
        var clientId = $"orders-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        await this.CreateClientAsync(clientId, ComputeThumbprintHex(certificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.read");

        var response = await this._client.SendAsync(request);

        await AssertOAuthErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
    }

    [Fact]
    public async Task PostToken_ThumbprintMismatch_ReturnsInvalidClient()
    {
        var clientId = $"orders-{Guid.NewGuid():N}";
        using var registeredCertificate = CreateCertificate(clientId);
        using var presentedCertificate = CreateCertificate($"{clientId}-presented");
        await this.CreateClientAsync(clientId, ComputeThumbprintHex(registeredCertificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(presentedCertificate.RawData));

        var response = await this._client.SendAsync(request);

        await AssertOAuthErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
    }

    [Fact]
    public async Task PostToken_UnassignedScope_ReturnsInvalidScope()
    {
        var clientId = $"orders-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        await this.CreateClientAsync(clientId, ComputeThumbprintHex(certificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.write");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));

        var response = await this._client.SendAsync(request);

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "invalid_scope");
    }

    [Fact]
    public async Task PostToken_UnsupportedGrantType_ReturnsUnsupportedGrantType()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/connect/token", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("client_id", "orders-service"),
            ]),
        };

        var response = await this._client.SendAsync(request);

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "unsupported_grant_type");
    }

    private static HttpRequestMessage CreateTokenRequest(string clientId, string scope)
    {
        return new HttpRequestMessage(HttpMethod.Post, new Uri("/connect/token", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("scope", scope),
            ]),
        };
    }

    private static X509Certificate2 CreateCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string ComputeThumbprintHex(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static string ComputeThumbprintBase64Url(X509Certificate2 certificate)
    {
        return Base64UrlEncode(SHA256.HashData(certificate.RawData));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
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

    private static async Task AssertOAuthErrorAsync(HttpResponseMessage response, HttpStatusCode statusCode, string error)
    {
        Assert.Equal(statusCode, response.StatusCode);
        var oauthError = await response.Content.ReadFromJsonAsync<OAuthErrorResponse>(_jsonOptions);
        Assert.NotNull(oauthError);
        Assert.Equal(error, oauthError.Error);
    }

    private async Task<ClientResponse> CreateClientAsync(
        string clientId,
        string certificateThumbprint,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> roles)
    {
        var request = new CreateClientRequest
        {
            ClientId = clientId,
            CertificateThumbprintSha256 = certificateThumbprint,
            AssignedScopes = scopes,
            AssignedRoles = roles,
        };
        using var adminClient = this._factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Api-Key", TestWebApplicationFactory.TestApiKey);
        var response = await adminClient.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
    }
}
