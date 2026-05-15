namespace Backend.Application.Models.Scopes;

public sealed class CreateScopeRequest
{
    public string? Value { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public bool? Active { get; init; }
}
