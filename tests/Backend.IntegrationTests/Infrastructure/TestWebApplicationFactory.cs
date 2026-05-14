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

    public TestWebApplicationFactory()
    {
        this._dbPath = Path.Combine(Path.GetTempPath(), $"idm_test_{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("AdminApi:ApiKey", TestApiKey);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={this._dbPath}");

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
    }
}
