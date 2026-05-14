using System.Diagnostics.CodeAnalysis;

namespace Backend.Domain.Entities;

public sealed class MachineClient
{
    [ExcludeFromCodeCoverage]
    private MachineClient()
    {
        this.ClientId = string.Empty;
        this.AssignedScopeValues = string.Empty;
        this.AssignedRoleValues = string.Empty;
    }

    public Guid Id { get; private set; }

    public string ClientId { get; private set; }

    public string? DisplayName { get; private set; }

    public bool Active { get; private set; }

    public string? CertificateThumbprintSha256 { get; private set; }

    public string? CertificateSubject { get; private set; }

    public DateTimeOffset? CertificateExpiresAt { get; private set; }

    public string AssignedScopeValues { get; private set; }

    public string AssignedRoleValues { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MachineClient Create(string clientId, string? displayName)
    {
        return new MachineClient
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            DisplayName = displayName,
            AssignedScopeValues = string.Empty,
            AssignedRoleValues = string.Empty,
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

    public void UpdateCertificate(string? thumbprintSha256, string? subject, DateTimeOffset? expiresAt)
    {
        this.CertificateThumbprintSha256 = thumbprintSha256;
        this.CertificateSubject = subject;
        this.CertificateExpiresAt = expiresAt;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignScopes(IReadOnlyCollection<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        this.AssignedScopeValues = string.Join(' ', scopes);
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignRoles(IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        this.AssignedRoleValues = string.Join(' ', roles);
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<string> GetAssignedScopes()
    {
        return SplitValues(this.AssignedScopeValues);
    }

    public IReadOnlyList<string> GetAssignedRoles()
    {
        return SplitValues(this.AssignedRoleValues);
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

    private static string[] SplitValues(string values)
    {
        return values.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
