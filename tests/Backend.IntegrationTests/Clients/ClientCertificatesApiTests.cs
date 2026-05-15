using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.Clients;

public sealed class ClientCertificatesApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClientCertificatesApiTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._factory = factory;
        this._client = factory.CreateClient();
        this._client.DefaultRequestHeaders.Add("X-Api-Key", TestWebApplicationFactory.TestApiKey);
    }

    [Fact]
    public async Task Post_CsrRequest_IssuesCertificate()
    {
        var client = await this.CreateClientAsync($"csr-{Guid.NewGuid():N}");
        using var rsa = RSA.Create(2048);
        var csrPem = CreateCsrPem(client.ClientId, rsa);
        var request = new CreateCertificateRequest
        {
            Mode = "csr",
            CertificateSigningRequestPem = csrPem,
            DisplayName = "rotation cert",
            ValidityDays = 30,
        };

        var response = await this._client.PostAsJsonAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates", UriKind.Relative),
            request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var certificate = await ReadJsonAsync<CertificateResponse>(response);
        Assert.Equal(client.ClientId, certificate.ClientId);
        Assert.Equal("rotation cert", certificate.DisplayName);
        Assert.Equal("Active", certificate.Status);
        Assert.Contains("BEGIN CERTIFICATE", certificate.CertificatePem, StringComparison.Ordinal);
        Assert.Equal(64, certificate.ThumbprintSha256.Length);
        Assert.Contains("IdmDemo Local Development CA", certificate.Issuer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_ExternalCertificate_StoresFullPemInListAndGet()
    {
        var client = await this.CreateClientAsync($"external-{Guid.NewGuid():N}");
        using var certificate = CreateSelfSignedCertificate(client.ClientId);
        var certificatePem = certificate.ExportCertificatePem();
        var createRequest = new CreateCertificateRequest
        {
            Mode = "external",
            CertificatePem = certificatePem,
            DisplayName = "external cert",
        };

        var createResponse = await this._client.PostAsJsonAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates", UriKind.Relative),
            createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await ReadJsonAsync<CertificateResponse>(createResponse);

        var listResponse = await this._client.GetAsync(new Uri($"/scim/v2/Clients/{client.Id}/Certificates", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadJsonAsync<ScimListResponse<CertificateResponse>>(listResponse);
        var listed = Assert.Single(list.Resources);
        Assert.Equal(certificatePem, listed.CertificatePem);

        var getResponse = await this._client.GetAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await ReadJsonAsync<CertificateResponse>(getResponse);
        Assert.Equal(certificatePem, fetched.CertificatePem);
    }

    [Fact]
    public async Task GetCertificateAuthority_RequiresAdminApiKey()
    {
        using var anonymousClient = this._factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync(new Uri("/scim/v2/Certificates/Authority", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var response = await this._client.GetAsync(new Uri("/scim/v2/Certificates/Authority", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authority = await ReadJsonAsync<CertificateAuthorityResponse>(response);
        Assert.Contains("BEGIN CERTIFICATE", authority.CertificatePem, StringComparison.Ordinal);
        Assert.Contains("IdmDemo Local Development CA", authority.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenRequest_WithManagedCertificate_ReturnsAccessToken()
    {
        var client = await this.CreateClientAsync($"token-{Guid.NewGuid():N}", ["orders.read"]);
        using var certificate = await this.RegisterExternalCertificateAsync(client);

        using var request = CreateTokenRequest(client.ClientId, "orders.read");
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));

        using var tokenClient = this._factory.CreateClient();
        var response = await tokenClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await ReadJsonAsync<TokenResponse>(response);
        Assert.Equal("orders.read", token.Scope);
        Assert.NotEmpty(token.AccessToken);
    }

    [Fact]
    public async Task Revoke_IsIdempotentAndBlocksFutureTokenIssuance()
    {
        var client = await this.CreateClientAsync($"revoke-{Guid.NewGuid():N}", ["orders.read"]);
        using var certificate = await this.RegisterExternalCertificateAsync(client);
        var listResponse = await this._client.GetAsync(new Uri($"/scim/v2/Clients/{client.Id}/Certificates", UriKind.Relative));
        var list = await ReadJsonAsync<ScimListResponse<CertificateResponse>>(listResponse);
        var certificateId = Assert.Single(list.Resources).Id;
        var revokeRequest = new RevokeCertificateRequest { Reason = "rotated" };

        var firstRevoke = await this._client.PostAsJsonAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates/{certificateId}/Revoke", UriKind.Relative),
            revokeRequest);
        Assert.Equal(HttpStatusCode.OK, firstRevoke.StatusCode);
        var firstRevoked = await ReadJsonAsync<CertificateResponse>(firstRevoke);
        Assert.Equal("Revoked", firstRevoked.Status);

        var secondRevoke = await this._client.PostAsJsonAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates/{certificateId}/Revoke", UriKind.Relative),
            revokeRequest);
        Assert.Equal(HttpStatusCode.OK, secondRevoke.StatusCode);
        var secondRevoked = await ReadJsonAsync<CertificateResponse>(secondRevoke);
        Assert.Equal("Revoked", secondRevoked.Status);

        using var tokenRequest = CreateTokenRequest(client.ClientId, "orders.read");
        tokenRequest.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        using var tokenClient = this._factory.CreateClient();
        var tokenResponse = await tokenClient.SendAsync(tokenRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, tokenResponse.StatusCode);
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

    private static string CreateCsrPem(string subjectName, RSA rsa)
    {
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSigningRequestPem();
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, _jsonOptions)!;
    }

    private async Task<ClientResponse> CreateClientAsync(string clientId, IReadOnlyList<string>? assignedScopes = null)
    {
        foreach (var assignedScope in assignedScopes ?? [])
        {
            var scopeResponse = await this._client.PostAsJsonAsync(
                new Uri("/scim/v2/Scopes", UriKind.Relative),
                new CreateScopeRequest { Value = assignedScope });
            if (scopeResponse.StatusCode != HttpStatusCode.Conflict)
            {
                scopeResponse.EnsureSuccessStatusCode();
            }
        }

        var request = new CreateClientRequest
        {
            ClientId = clientId,
            AssignedScopes = assignedScopes ?? [],
        };
        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<ClientResponse>(response);
    }

    private async Task<X509Certificate2> RegisterExternalCertificateAsync(ClientResponse client)
    {
        var certificate = CreateSelfSignedCertificate(client.ClientId);
        var createRequest = new CreateCertificateRequest
        {
            Mode = "external",
            CertificatePem = certificate.ExportCertificatePem(),
            DisplayName = "token cert",
        };

        var response = await this._client.PostAsJsonAsync(
            new Uri($"/scim/v2/Clients/{client.Id}/Certificates", UriKind.Relative),
            createRequest);
        response.EnsureSuccessStatusCode();
        return certificate;
    }
}
