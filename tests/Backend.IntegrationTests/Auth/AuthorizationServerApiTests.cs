using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scopes;
using Backend.Application.Services;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal(["ES256", "RS256"], metadata.DpopSigningAlgValuesSupported);
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
    public async Task PostToken_EscapedForwardedPemCertificate_ReturnsCertificateBoundJwt()
    {
        var clientId = $"nginx-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        await this.CreateClientAsync(clientId, ComputeThumbprintHex(certificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Uri.EscapeDataString(certificate.ExportCertificatePem()));

        var response = await this._client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        Assert.Equal("Bearer", tokenResponse.TokenType);

        var payload = ReadJwtPayload(tokenResponse.AccessToken);
        Assert.Equal(
            ComputeThumbprintBase64Url(certificate),
            payload.GetProperty("cnf").GetProperty("x5t#S256").GetString());
    }

    [Fact]
    public async Task PostToken_ValidDpopProof_ReturnsDpopBoundJwt()
    {
        var clientId = $"dpop-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        using var dpopKey = RSA.Create(2048);
        var client = await this.CreateClientAsync(
            clientId,
            ComputeThumbprintHex(certificate),
            ["orders.read"],
            []);
        using var request = CreateTokenRequest(client.ClientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        request.Headers.Add("DPoP", CreateDpopProof(dpopKey));

        var response = await this._client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        Assert.Equal("DPoP", tokenResponse.TokenType);

        var payload = ReadJwtPayload(tokenResponse.AccessToken);
        var confirmation = payload.GetProperty("cnf");
        Assert.Equal(ComputeJwkThumbprint(dpopKey), confirmation.GetProperty("jkt").GetString());
        Assert.False(confirmation.TryGetProperty("x5t#S256", out _));
    }

    [Fact]
    public async Task DpopBoundToken_WithProtectedResourceProof_ValidatesAccessToken()
    {
        var clientId = $"dpop-resource-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        using var dpopKey = RSA.Create(2048);
        var client = await this.CreateClientAsync(
            clientId,
            ComputeThumbprintHex(certificate),
            ["orders.read"],
            []);
        using var request = CreateTokenRequest(client.ClientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        request.Headers.Add("DPoP", CreateDpopProof(dpopKey));
        var tokenResponseMessage = await this._client.SendAsync(request);
        tokenResponseMessage.EnsureSuccessStatusCode();
        var tokenResponse = await tokenResponseMessage.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        var resourceUri = new Uri("https://idmdemo.test/scim/v2/Users");
        var protectedResourceProof = CreateDpopProof(dpopKey, "GET", resourceUri, tokenResponse.AccessToken);

        using var scope = this._factory.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDpopBoundAccessTokenValidator>();
        var validatedToken = await validator.ValidateAsync(
            tokenResponse.AccessToken,
            protectedResourceProof,
            "GET",
            resourceUri);

        Assert.Equal(client.Id, validatedToken.Subject);
        Assert.Equal(clientId, validatedToken.ClientId);
        Assert.Equal("orders.read", validatedToken.Scope);
        Assert.Equal(ComputeJwkThumbprint(dpopKey), validatedToken.DpopJwkThumbprint);
        Assert.Null(validatedToken.CertificateThumbprintSha256);
    }

    [Fact]
    public async Task DpopBoundToken_WithMismatchedProtectedResourceProof_ReturnsInvalidToken()
    {
        var clientId = $"dpop-resource-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        using var tokenKey = RSA.Create(2048);
        using var resourceKey = RSA.Create(2048);
        var client = await this.CreateClientAsync(
            clientId,
            ComputeThumbprintHex(certificate),
            ["orders.read"],
            []);
        using var request = CreateTokenRequest(client.ClientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        request.Headers.Add("DPoP", CreateDpopProof(tokenKey));
        var tokenResponseMessage = await this._client.SendAsync(request);
        tokenResponseMessage.EnsureSuccessStatusCode();
        var tokenResponse = await tokenResponseMessage.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        var resourceUri = new Uri("https://idmdemo.test/scim/v2/Users");
        var protectedResourceProof = CreateDpopProof(resourceKey, "GET", resourceUri, tokenResponse.AccessToken);

        using var scope = this._factory.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDpopBoundAccessTokenValidator>();
        var exception = await Assert.ThrowsAsync<OAuthException>(() => validator.ValidateAsync(
            tokenResponse.AccessToken,
            protectedResourceProof,
            "GET",
            resourceUri));

        Assert.Equal("invalid_token", exception.Error);
        Assert.Equal(HttpStatusCode.Unauthorized, (HttpStatusCode)exception.StatusCode);
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

    [Fact]
    public async Task PostToken_RequireDpopWithoutProof_ReturnsInvalidDpopProof()
    {
        await using var factory = TestWebApplicationFactory.CreateRequireDpop();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var clientId = $"required-dpop-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        await CreateClientAsync(factory, clientId, ComputeThumbprintHex(certificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));

        var response = await client.SendAsync(request);

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "invalid_dpop_proof");
    }

    [Fact]
    public async Task PostToken_RequireDpopWithValidProof_ReturnsDpopToken()
    {
        await using var factory = TestWebApplicationFactory.CreateRequireDpop();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var clientId = $"required-dpop-{Guid.NewGuid():N}";
        using var certificate = CreateCertificate(clientId);
        using var dpopKey = RSA.Create(2048);
        await CreateClientAsync(factory, clientId, ComputeThumbprintHex(certificate), ["orders.read"], []);
        using var request = CreateTokenRequest(clientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        request.Headers.Add("DPoP", CreateDpopProof(dpopKey));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions);
        Assert.NotNull(tokenResponse);
        Assert.Equal("DPoP", tokenResponse.TokenType);
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

    private static string CreateDpopProof(
        RSA rsa,
        string httpMethod = "POST",
        Uri? httpUri = null,
        string? accessToken = null)
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
        httpUri ??= new Uri("https://idmdemo.test/connect/token");
        var payload = new Dictionary<string, object?>
        {
            ["htm"] = httpMethod,
            ["htu"] = httpUri.ToString(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (!string.IsNullOrEmpty(accessToken))
        {
            payload["ath"] = ComputeAccessTokenHash(accessToken);
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

    private static string ComputeThumbprintHex(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static string ComputeThumbprintBase64Url(X509Certificate2 certificate)
    {
        return Base64UrlEncode(SHA256.HashData(certificate.RawData));
    }

    private static string ComputeJwkThumbprint(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var canonicalJson = string.Create(
            null,
            $"{{\"e\":\"{Base64UrlEncode(parameters.Exponent ?? [])}\",\"kty\":\"RSA\",\"n\":\"{Base64UrlEncode(parameters.Modulus ?? [])}\"}}");

        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    private static string ComputeAccessTokenHash(string accessToken)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
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

    private static async Task<ClientResponse> CreateClientAsync(
        TestWebApplicationFactory factory,
        string clientId,
        string certificateThumbprint,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> roles)
    {
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.AdminBearerToken);

        foreach (var scope in scopes)
        {
            await CreateScopeAsync(adminClient, scope);
        }

        foreach (var role in roles)
        {
            await CreateRoleAsync(adminClient, role);
        }

        var request = new CreateClientRequest
        {
            ClientId = clientId,
            CertificateThumbprintSha256 = certificateThumbprint,
            AssignedScopes = scopes,
            AssignedRoles = roles,
        };
        var response = await adminClient.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
    }

    private static async Task CreateRoleAsync(HttpClient adminClient, string value)
    {
        var response = await adminClient.PostAsJsonAsync(
            new Uri("/scim/v2/Roles", UriKind.Relative),
            new CreateRoleRequest { Value = value });
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task CreateScopeAsync(HttpClient adminClient, string value)
    {
        var response = await adminClient.PostAsJsonAsync(
            new Uri("/scim/v2/Scopes", UriKind.Relative),
            new CreateScopeRequest { Value = value });
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<ClientResponse> CreateClientAsync(
        string clientId,
        string certificateThumbprint,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> roles)
    {
        return await CreateClientAsync(this._factory, clientId, certificateThumbprint, scopes, roles);
    }
}
