namespace Backend.Application.Models.Users;

public sealed class CreateUserRequest
{
    public string? UserName { get; init; }

    public string? DisplayName { get; init; }

    public string? ExternalId { get; init; }

    public bool? Active { get; init; }

    public IReadOnlyList<string> AssignedRoles { get; init; } = [];
}
