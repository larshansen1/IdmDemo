namespace Backend.Application.Models.Clients;

public sealed class CreateClientRequest
{
    public string? ClientId { get; init; }

    public string? DisplayName { get; init; }

    public bool? Active { get; init; }
}
