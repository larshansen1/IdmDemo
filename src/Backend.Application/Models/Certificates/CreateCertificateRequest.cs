namespace Backend.Application.Models.Certificates;

public sealed class CreateCertificateRequest
{
    public string? Mode { get; init; }

    public string? CertificateSigningRequestPem { get; init; }

    public string? CertificatePem { get; init; }

    public string? DisplayName { get; init; }

    public int? ValidityDays { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
