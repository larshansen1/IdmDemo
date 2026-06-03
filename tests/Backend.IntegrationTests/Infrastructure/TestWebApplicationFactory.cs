using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Domain.Entities;
using Backend.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Backend.IntegrationTests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string _adminClientId = "idm-mcp-admin";

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _dbPath;
    private readonly string _signingKeyPath;
    private readonly string _certificateAuthorityPath;
    private readonly bool _requireDpop;
    private readonly int? _tokenRateLimitPermitLimit;

    public TestWebApplicationFactory()
        : this(requireDpop: false, tokenRateLimitPermitLimit: null)
    {
    }

    private TestWebApplicationFactory(bool requireDpop, int? tokenRateLimitPermitLimit)
    {
        this._dbPath = Path.Combine(Path.GetTempPath(), $"idm_test_{Guid.NewGuid():N}.db");
        this._signingKeyPath = Path.Combine(Path.GetTempPath(), $"idm_test_signing_{Guid.NewGuid():N}.json");
        this._certificateAuthorityPath = Path.Combine(Path.GetTempPath(), $"idm_test_ca_{Guid.NewGuid():N}.json");
        this._requireDpop = requireDpop;
        this._tokenRateLimitPermitLimit = tokenRateLimitPermitLimit;
    }

    public string AdminBearerToken { get; private set; } = string.Empty;

    public static TestWebApplicationFactory CreateRequireDpop()
    {
        return new TestWebApplicationFactory(requireDpop: true, tokenRateLimitPermitLimit: null);
    }

    public static TestWebApplicationFactory CreateWithTokenRateLimit(int permitLimit)
    {
        return new TestWebApplicationFactory(requireDpop: false, tokenRateLimitPermitLimit: permitLimit);
    }

    public async Task InitializeAsync()
    {
        this.AdminBearerToken = await this.SeedAdminMachineClientAsync().ConfigureAwait(false);
    }

    Task IAsyncLifetime.InitializeAsync() => this.InitializeAsync();

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    internal async Task<string> SeedAdminMachineClientAsync()
    {
        using var scope = this.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = GlobalRole.Create("scim.admin", "SCIM Admin", "Full SCIM administration access");
        db.GlobalRoles.Add(role);

        using var cert = CreateSelfSignedCertificate(_adminClientId);
        var thumbprintHex = Convert.ToHexString(SHA256.HashData(cert.RawData));

        var machineClient = MachineClient.Create(_adminClientId, "MCP Admin (test)");
        machineClient.UpdateCertificate(thumbprintHex, cert.Subject, cert.NotAfter);
        machineClient.AssignRoles(["scim.admin"]);
        db.MachineClients.Add(machineClient);
        await db.SaveChangesAsync().ConfigureAwait(false);

        using var httpClient = this.CreateClient();
        httpClient.DefaultRequestHeaders.Add("X-Client-Cert", Convert.ToBase64String(cert.RawData));

        var tokenUri = new Uri("https://idmdemo.test/connect/token");
        using var tokenContent = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("grant_type", "client_credentials"),
            KeyValuePair.Create("client_id", _adminClientId),
        ]);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/connect/token", UriKind.Relative));
        tokenRequest.Content = tokenContent;

        if (this._requireDpop)
        {
            using var dpopKey = RSA.Create(2048);
            tokenRequest.Headers.Add("DPoP", CreateDpopProof(dpopKey, "POST", tokenUri));
        }

        var tokenResponse = await httpClient.SendAsync(tokenRequest).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();

        var tokenData = await tokenResponse.Content
            .ReadFromJsonAsync<TokenResponse>(_jsonOptions)
            .ConfigureAwait(false);

        return tokenData!.AccessToken;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Default", $"Data Source={this._dbPath}");
        builder.UseSetting("ScimAdmin:SeedClientId", string.Empty);
        builder.UseSetting("AuthorizationServer:Issuer", "https://idmdemo.test");
        builder.UseSetting("AuthorizationServer:Audience", "idm-demo-api");
        builder.UseSetting("AuthorizationServer:AccessTokenLifetimeSeconds", "3600");
        builder.UseSetting("AuthorizationServer:RequireDpop", this._requireDpop.ToString());
        builder.UseSetting("AuthorizationServer:SigningKeyPath", this._signingKeyPath);
        builder.UseSetting("AuthorizationServer:EnableForwardedClientCertificate", "true");
        builder.UseSetting("AuthorizationServer:ForwardedClientCertificateHeader", "X-Client-Cert");
        builder.UseSetting("CertificateAuthority:KeyPath", this._certificateAuthorityPath);
        if (this._tokenRateLimitPermitLimit is int tokenRateLimitPermitLimit)
        {
            builder.UseSetting(
                "RateLimiting:TokenEndpoint:PermitLimit",
                tokenRateLimitPermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:TokenEndpoint:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:TokenEndpoint:SegmentsPerWindow", "1");
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={this._dbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(this._dbPath))
        {
            File.Delete(this._dbPath);
        }

        if (disposing && File.Exists(this._signingKeyPath))
        {
            File.Delete(this._signingKeyPath);
        }

        if (disposing && File.Exists(this._certificateAuthorityPath))
        {
            File.Delete(this._certificateAuthorityPath);
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string CreateDpopProof(RSA rsa, string httpMethod, Uri httpUri)
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
            ["htu"] = httpUri.ToString(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var signingInput = $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }
}
