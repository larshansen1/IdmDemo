using Backend.Application.Models.Scim;

namespace Backend.Application.Models.Roles;

public sealed class RoleResponse
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:idmdemo:params:scim:schemas:Role"];

    public string Id { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public bool Active { get; init; }

    public ScimMeta Meta { get; init; } = new();
}
