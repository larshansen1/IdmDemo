using Backend.Application.Models.Scim;

namespace Backend.Application.Models.Clients;

public sealed class ClientResponse
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:idmdemo:params:scim:schemas:extension:2.0:Client"];

    public string Id { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool Active { get; init; }

    public string? CertificateThumbprintSha256 { get; init; }

    public string? CertificateSubject { get; init; }

    public DateTimeOffset? CertificateExpiresAt { get; init; }

    public IReadOnlyList<string> AssignedScopes { get; init; } = [];

    public IReadOnlyList<string> AssignedRoles { get; init; } = [];

    public ScimMeta Meta { get; init; } = new();
}
