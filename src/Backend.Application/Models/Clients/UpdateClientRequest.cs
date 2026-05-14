namespace Backend.Application.Models.Clients;

public sealed class UpdateClientRequest
{
    public string? ClientId { get; init; }

    public string? DisplayName { get; init; }

    public bool Active { get; init; } = true;
}
