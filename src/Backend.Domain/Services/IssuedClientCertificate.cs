namespace Backend.Domain.Services;

public sealed class IssuedClientCertificate
{
    public string CertificatePem { get; init; } = string.Empty;

    public string ThumbprintSha256 { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
