using System.Diagnostics.CodeAnalysis;

namespace Backend.Idp.Domain.Entities;

public sealed class GlobalScope
{
    [ExcludeFromCodeCoverage]
    private GlobalScope()
    {
        this.Value = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Value { get; private set; }

    public string? DisplayName { get; private set; }

    public string? Description { get; private set; }

    public bool Active { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static GlobalScope Create(string value, string? displayName, string? description)
    {
        return new GlobalScope
        {
            Id = Guid.NewGuid(),
            Value = value,
            DisplayName = displayName,
            Description = description,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string value, string? displayName, string? description, bool active)
    {
        this.Value = value;
        this.DisplayName = displayName;
        this.Description = description;
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
