namespace Backend.Application.Models.Scim;

public sealed class ScimError
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:ietf:params:scim:api:messages:2.0:Error"];

    public int Status { get; init; }

    public string? ScimType { get; init; }

    public string Detail { get; init; } = string.Empty;
}
