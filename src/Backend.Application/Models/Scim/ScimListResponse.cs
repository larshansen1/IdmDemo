namespace Backend.Application.Models.Scim;

public sealed class ScimListResponse<T>
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:ietf:params:scim:api:messages:2.0:ListResponse"];

    public int TotalResults { get; init; }

    public int StartIndex { get; init; } = 1;

    public int ItemsPerPage { get; init; }

    public IReadOnlyList<T> Resources { get; init; } = [];
}
