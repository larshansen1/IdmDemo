namespace Backend.Application.Models.Users;

public sealed class UpdateUserRequest
{
    public string? UserName { get; init; }

    public string? DisplayName { get; init; }

    public string? ExternalId { get; init; }

    public bool Active { get; init; } = true;

    public IReadOnlyList<string> AssignedRoles { get; init; } = [];
}
