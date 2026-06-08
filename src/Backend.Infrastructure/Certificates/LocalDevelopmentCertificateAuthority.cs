using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Backend.Idp.Domain.Exceptions;
using Backend.Idp.Domain.Services;

namespace Backend.Infrastructure.Certificates;

public sealed class LocalDevelopmentCertificateAuthority : ILocalCertificateAuthority, IDisposable
{
    private const int _caLifetimeYears = 5;
    private readonly string _certificateAuthorityPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalDevelopmentCertificateAuthority(string certificateAuthorityPath)
    {
        this._certificateAuthorityPath = certificateAuthorityPath;
    }

    public async Task<CertificateAuthorityCertificate> GetCertificateAsync(CancellationToken cancellationToken = default)
    {
        using var certificate = await this.GetOrCreateCertificateAuthorityAsync(cancellationToken).ConfigureAwait(false);
        return new CertificateAuthorityCertificate
        {
            CertificatePem = certificate.ExportCertificatePem(),
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            SerialNumber = certificate.SerialNumber,
            NotBefore = ToDateTimeOffset(certificate.NotBefore),
            ExpiresAt = ToDateTimeOffset(certificate.NotAfter),
        };
    }

    public async Task<IssuedClientCertificate> IssueCertificateAsync(
        string certificateSigningRequestPem,
        int validityDays,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateSigningRequestPem);

        using var issuer = await this.GetOrCreateCertificateAuthorityAsync(cancellationToken).ConfigureAwait(false);
        var request = LoadCertificateRequest(certificateSigningRequestPem);
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddDays(validityDays);
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var issuerKey = issuer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificate authority is missing an RSA private key.");
        var generator = X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1);

        using var issuedCertificate = request.Create(issuer.SubjectName, generator, notBefore, notAfter, serialNumber);
        return new IssuedClientCertificate
        {
            CertificatePem = issuedCertificate.ExportCertificatePem(),
            ThumbprintSha256 = ComputeThumbprint(issuedCertificate),
            Subject = issuedCertificate.Subject,
            Issuer = issuedCertificate.Issuer,
            SerialNumber = issuedCertificate.SerialNumber,
            NotBefore = ToDateTimeOffset(issuedCertificate.NotBefore),
            ExpiresAt = ToDateTimeOffset(issuedCertificate.NotAfter),
        };
    }

    public void Dispose()
    {
        this._lock.Dispose();
    }

    private static CertificateRequest LoadCertificateRequest(string certificateSigningRequestPem)
    {
        try
        {
            return CertificateRequest.LoadSigningRequestPem(
                certificateSigningRequestPem,
                HashAlgorithmName.SHA256);
        }
        catch (CryptographicException exception)
        {
            throw new ValidationException(
                "certificateSigningRequestPem must contain a valid PEM-encoded certificate signing request.",
                exception);
        }
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero);
    }

    private static string ComputeThumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static X509Certificate2 CreateCertificateAuthority()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=IdmDemo Local Development CA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(_caLifetimeYears);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static async Task<X509Certificate2> ReadCertificateAuthorityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var storedCertificate = await JsonSerializer.DeserializeAsync<StoredCertificateAuthority>(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Stored certificate authority could not be read.");

        using var certificate = X509Certificate2.CreateFromPem(storedCertificate.CertificatePem);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(storedCertificate.PrivateKeyPem);
        return certificate.CopyWithPrivateKey(rsa);
    }

    private static async Task WriteCertificateAuthorityAsync(
        string path,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var rsa = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificate authority is missing an RSA private key.");
        using (rsa)
        {
            var storedCertificate = new StoredCertificateAuthority
            {
                CertificatePem = certificate.ExportCertificatePem(),
                PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            };

            var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = File.Open(path, fileOptions);
            await JsonSerializer.SerializeAsync(stream, storedCertificate, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<X509Certificate2> GetOrCreateCertificateAuthorityAsync(CancellationToken cancellationToken)
    {
        await this._lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(this._certificateAuthorityPath))
            {
                return await ReadCertificateAuthorityAsync(this._certificateAuthorityPath, cancellationToken).ConfigureAwait(false);
            }

            using var certificate = CreateCertificateAuthority();
            await WriteCertificateAuthorityAsync(this._certificateAuthorityPath, certificate, cancellationToken).ConfigureAwait(false);
            return await ReadCertificateAuthorityAsync(this._certificateAuthorityPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._lock.Release();
        }
    }

    private sealed class StoredCertificateAuthority
    {
        public string CertificatePem { get; init; } = string.Empty;

        public string PrivateKeyPem { get; init; } = string.Empty;
    }
}
