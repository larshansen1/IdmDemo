using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Auth;
using Backend.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Backend.IntegrationTests.Startup;

public sealed class ScimAdminSeederTests
{
    [Fact]
    public async Task StartAsync_WithGenerateCertIfMissing_SeedsAdminClientAndCertificate()
    {
        var clientId = $"seed-admin-{Guid.NewGuid():N}";
        await using var factory = new SeededAdminFactory(clientId, generateCertIfMissing: true);
        using var httpClient = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.GlobalRoles.SingleAsync(r => r.Value == "scim.admin");
        var machineClient = await db.MachineClients.SingleAsync(c => c.ClientId == clientId);

        Assert.Equal("SCIM Admin", role.DisplayName);
        Assert.Contains("scim.admin", machineClient.GetAssignedRoles());
        Assert.NotNull(machineClient.CertificateThumbprintSha256);
        Assert.True(File.Exists(factory.SeedCertPath));

        using var certificate = X509Certificate2.CreateFromPemFile(factory.SeedCertPath);
        Assert.Equal(certificate.Subject, machineClient.CertificateSubject);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            machineClient.CertificateThumbprintSha256);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/connect/token", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                KeyValuePair.Create("grant_type", "client_credentials"),
                KeyValuePair.Create("client_id", clientId),
            ]),
        };
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
    }

    [Fact]
    public async Task StartAsync_WithMissingCertAndGenerationDisabled_SeedsClientWithoutCertificate()
    {
        var clientId = $"seed-admin-{Guid.NewGuid():N}";
        await using var factory = new SeededAdminFactory(clientId, generateCertIfMissing: false);
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var machineClient = await db.MachineClients.SingleAsync(c => c.ClientId == clientId);

        Assert.Contains("scim.admin", machineClient.GetAssignedRoles());
        Assert.Null(machineClient.CertificateThumbprintSha256);
        Assert.False(File.Exists(factory.SeedCertPath));
    }

    private sealed class SeededAdminFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"idm_seed_test_{Guid.NewGuid():N}.db");
        private readonly string _signingKeyPath = Path.Combine(Path.GetTempPath(), $"idm_seed_signing_{Guid.NewGuid():N}.json");
        private readonly string _certificateAuthorityPath = Path.Combine(Path.GetTempPath(), $"idm_seed_ca_{Guid.NewGuid():N}.json");
        private readonly string _clientId;
        private readonly bool _generateCertIfMissing;

        public SeededAdminFactory(string clientId, bool generateCertIfMissing)
        {
            this._clientId = clientId;
            this._generateCertIfMissing = generateCertIfMissing;
            this.SeedCertPath = Path.Combine(Path.GetTempPath(), $"idm_seed_admin_{Guid.NewGuid():N}.pem");
        }

        public string SeedCertPath { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseSetting("ConnectionStrings:Default", $"Data Source={this._dbPath}");
            builder.UseSetting("ScimAdmin:SeedClientId", this._clientId);
            builder.UseSetting("ScimAdmin:SeedCertPath", this.SeedCertPath);
            builder.UseSetting("ScimAdmin:GenerateCertIfMissing", this._generateCertIfMissing.ToString());
            builder.UseSetting("AuthorizationServer:Issuer", "https://idmdemo.test");
            builder.UseSetting("AuthorizationServer:Audience", "idm-demo-api");
            builder.UseSetting("AuthorizationServer:AccessTokenLifetimeSeconds", "300");
            builder.UseSetting("AuthorizationServer:RequireDpop", "false");
            builder.UseSetting("AuthorizationServer:SigningKeyPath", this._signingKeyPath);
            builder.UseSetting("AuthorizationServer:EnableForwardedClientCertificate", "true");
            builder.UseSetting("AuthorizationServer:ForwardedClientCertificateHeader", "X-Client-Cert");
            builder.UseSetting("AuthorizationServer:TrustedProxies:0", "127.0.0.1");
            builder.UseSetting("AuthorizationServer:TrustedProxies:1", "::1");
            builder.UseSetting("CertificateAuthority:KeyPath", this._certificateAuthorityPath);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={this._dbPath}"));

                services.AddSingleton<IStartupFilter>(new SetLoopbackRemoteIpFilter());
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            DeleteIfExists(this._dbPath);
            DeleteIfExists(this._signingKeyPath);
            DeleteIfExists(this._certificateAuthorityPath);
            DeleteIfExists(this.SeedCertPath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
