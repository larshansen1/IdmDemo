namespace Backend.Domain.Services;

public interface ILocalCertificateAuthority
{
    Task<CertificateAuthorityCertificate> GetCertificateAsync(CancellationToken cancellationToken = default);

    Task<IssuedClientCertificate> IssueCertificateAsync(
        string certificateSigningRequestPem,
        int validityDays,
        CancellationToken cancellationToken = default);
}
