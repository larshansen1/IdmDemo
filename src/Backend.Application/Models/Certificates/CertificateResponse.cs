using Backend.Application.Models.Scim;

namespace Backend.Application.Models.Certificates;

public sealed class CertificateResponse
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:idmdemo:params:scim:schemas:extension:2.0:ClientCertificate"];

    public string Id { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string ThumbprintSha256 { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public string CertificatePem { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? RevokedAt { get; init; }

    public string? RevocationReason { get; init; }

    public ScimMeta Meta { get; init; } = new();
}
