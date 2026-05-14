using Backend.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    private readonly string _dbPath;
    private readonly string _signingKeyPath;
    private readonly string _certificateAuthorityPath;

    public TestWebApplicationFactory()
    {
        this._dbPath = Path.Combine(Path.GetTempPath(), $"idm_test_{Guid.NewGuid():N}.db");
        this._signingKeyPath = Path.Combine(Path.GetTempPath(), $"idm_test_signing_{Guid.NewGuid():N}.json");
        this._certificateAuthorityPath = Path.Combine(Path.GetTempPath(), $"idm_test_ca_{Guid.NewGuid():N}.json");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("AdminApi:ApiKey", TestApiKey);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={this._dbPath}");
        builder.UseSetting("AuthorizationServer:Issuer", "https://idmdemo.test");
        builder.UseSetting("AuthorizationServer:Audience", "idm-demo-api");
        builder.UseSetting("AuthorizationServer:AccessTokenLifetimeSeconds", "3600");
        builder.UseSetting("AuthorizationServer:SigningKeyPath", this._signingKeyPath);
        builder.UseSetting("AuthorizationServer:EnableForwardedClientCertificate", "true");
        builder.UseSetting("AuthorizationServer:ForwardedClientCertificateHeader", "X-Client-Cert");
        builder.UseSetting("CertificateAuthority:KeyPath", this._certificateAuthorityPath);

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
}
