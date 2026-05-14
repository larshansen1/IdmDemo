namespace Backend.Application.Models.Clients;

public sealed class UpdateClientRequest
{
    public string? ClientId { get; init; }

    public string? DisplayName { get; init; }

    public bool Active { get; init; } = true;

    public string? CertificateThumbprintSha256 { get; init; }

    public string? CertificateSubject { get; init; }

    public DateTimeOffset? CertificateExpiresAt { get; init; }

    public IReadOnlyList<string> AssignedScopes { get; init; } = [];

    public IReadOnlyList<string> AssignedRoles { get; init; } = [];
}
