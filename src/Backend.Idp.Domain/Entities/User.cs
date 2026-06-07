using System.Diagnostics.CodeAnalysis;

namespace Backend.Idp.Domain.Entities;

public sealed class User
{
    [ExcludeFromCodeCoverage]
    private User()
    {
        this.UserName = string.Empty;
        this.AssignedRoleValues = string.Empty;
    }

    public Guid Id { get; private set; }

    public string? ExternalId { get; private set; }

    public string UserName { get; private set; }

    public string? DisplayName { get; private set; }

    public bool Active { get; private set; }

    public string AssignedRoleValues { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(string userName, string? displayName, string? externalId)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = displayName,
            ExternalId = externalId,
            AssignedRoleValues = string.Empty,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string userName, string? displayName, string? externalId, bool active)
    {
        this.UserName = userName;
        this.DisplayName = displayName;
        this.ExternalId = externalId;
        this.Active = active;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignRoles(IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        this.AssignedRoleValues = string.Join(' ', roles);
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<string> GetAssignedRoles()
    {
        return this.AssignedRoleValues.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
