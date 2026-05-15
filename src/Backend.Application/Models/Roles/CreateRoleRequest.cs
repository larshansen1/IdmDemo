namespace Backend.Application.Models.Roles;

public sealed class CreateRoleRequest
{
    public string? Value { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public bool? Active { get; init; }
}
