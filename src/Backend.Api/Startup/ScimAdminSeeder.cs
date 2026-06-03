using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Domain.Entities;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Startup;

public sealed partial class ScimAdminSeeder : IHostedService
{
    private const string _roleName = "scim.admin";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScimAdminSeeder> _logger;

    public ScimAdminSeeder(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ScimAdminSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        this._serviceProvider = serviceProvider;
        this._configuration = configuration;
        this._logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seedClientId = this._configuration["ScimAdmin:SeedClientId"];
        if (string.IsNullOrWhiteSpace(seedClientId))
        {
            return;
        }

        var certPath = this._configuration["ScimAdmin:SeedCertPath"];

        using var scope = this._serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await this.EnsureRoleExistsAsync(db, cancellationToken).ConfigureAwait(false);
        await this.EnsureMachineClientExistsAsync(db, seedClientId, certPath, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded GlobalRole '{Role}'.")]
    private static partial void LogRoleSeeded(ILogger logger, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded MachineClient '{ClientId}' with role '{Role}'.")]
    private static partial void LogClientSeeded(ILogger logger, string clientId, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered certificate (thumbprint {Thumbprint}) for '{ClientId}'.")]
    private static partial void LogCertRegistered(ILogger logger, string thumbprint, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated self-signed certificate for '{ClientId}' at '{Path}'.")]
    private static partial void LogCertGenerated(ILogger logger, string clientId, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ScimAdmin:SeedCertPath '{Path}' does not exist; skipping cert registration.")]
    private static partial void LogCertNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load seed certificate from '{Path}'.")]
    private static partial void LogCertLoadFailed(ILogger logger, Exception ex, string path);

    private static void GenerateSelfSignedCertPem(string subjectName, string outputPath)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));

        var pem = cert.ExportCertificatePem() + "\n" + rsa.ExportRSAPrivateKeyPem();
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(outputPath, pem);
    }

    private async Task EnsureRoleExistsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var exists = await db.GlobalRoles
            .AnyAsync(r => r.Value == _roleName, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        db.GlobalRoles.Add(GlobalRole.Create(_roleName, "SCIM Admin", "Full SCIM administration access"));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogRoleSeeded(this._logger, _roleName);
    }

    private async Task EnsureMachineClientExistsAsync(
        AppDbContext db,
        string seedClientId,
        string? certPath,
        CancellationToken cancellationToken)
    {
        var client = await db.MachineClients
            .FirstOrDefaultAsync(c => c.ClientId == seedClientId, cancellationToken)
            .ConfigureAwait(false);

        if (client is null)
        {
            client = MachineClient.Create(seedClientId, "MCP Admin (seeded)");
            client.AssignRoles([_roleName]);
            db.MachineClients.Add(client);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogClientSeeded(this._logger, seedClientId, _roleName);
        }

        if (string.IsNullOrWhiteSpace(certPath) || client.CertificateThumbprintSha256 is not null)
        {
            return;
        }

        if (!File.Exists(certPath))
        {
            if (this._configuration.GetValue<bool>("ScimAdmin:GenerateCertIfMissing"))
            {
                GenerateSelfSignedCertPem(seedClientId, certPath);
                LogCertGenerated(this._logger, seedClientId, certPath);
            }
            else
            {
                LogCertNotFound(this._logger, certPath);
                return;
            }
        }

        try
        {
            using var cert = X509Certificate2.CreateFromPemFile(certPath);
            var thumbprint = Convert.ToHexString(SHA256.HashData(cert.RawData));
            client.UpdateCertificate(thumbprint, cert.Subject, cert.NotAfter);
            client.AssignRoles([_roleName]);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogCertRegistered(this._logger, thumbprint, seedClientId);
        }
        catch (CryptographicException ex)
        {
            LogCertLoadFailed(this._logger, ex, certPath);
        }
    }
}
