using Backend.Application.Models.Scim;

namespace Backend.Application.Models.Users;

public sealed class UserResponse
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:ietf:params:scim:schemas:core:2.0:User"];

    public string Id { get; init; } = string.Empty;

    public string? ExternalId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool Active { get; init; }

    public IReadOnlyList<string> AssignedRoles { get; init; } = [];

    public ScimMeta Meta { get; init; } = new();
}
