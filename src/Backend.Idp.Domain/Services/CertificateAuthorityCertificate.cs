namespace Backend.Idp.Domain.Services;

public sealed class CertificateAuthorityCertificate
{
    public string CertificatePem { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
