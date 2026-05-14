namespace Backend.Application.Models.Scim;

public sealed class ScimMeta
{
    public string ResourceType { get; init; } = string.Empty;

    public DateTimeOffset Created { get; init; }

    public DateTimeOffset LastModified { get; init; }

    public string Location { get; init; } = string.Empty;
}
