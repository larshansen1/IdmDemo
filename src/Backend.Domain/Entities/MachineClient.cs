using System.Diagnostics.CodeAnalysis;

namespace Backend.Domain.Entities;

public sealed class MachineClient
{
    [ExcludeFromCodeCoverage]
    private MachineClient()
    {
        this.ClientId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string ClientId { get; private set; }

    public string? DisplayName { get; private set; }

    public bool Active { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MachineClient Create(string clientId, string? displayName)
    {
        return new MachineClient
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            DisplayName = displayName,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string clientId, string? displayName, bool active)
    {
        this.ClientId = clientId;
        this.DisplayName = displayName;
        this.Active = active;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        this.Active = true;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        this.Active = false;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
