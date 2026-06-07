using System.Diagnostics.CodeAnalysis;

namespace Backend.Idp.Domain.Entities;

public sealed class MachineClientCertificate
{
    [ExcludeFromCodeCoverage]
    private MachineClientCertificate()
    {
        this.DisplayName = string.Empty;
        this.ThumbprintSha256 = string.Empty;
        this.Subject = string.Empty;
        this.Issuer = string.Empty;
        this.SerialNumber = string.Empty;
        this.CertificatePem = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid MachineClientId { get; private set; }

    public string? DisplayName { get; private set; }

    public string ThumbprintSha256 { get; private set; }

    public string Subject { get; private set; }

    public string Issuer { get; private set; }

    public string SerialNumber { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string CertificatePem { get; private set; }

    public MachineClientCertificateStatus Status { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static MachineClientCertificate Create(
        Guid machineClientId,
        string? displayName,
        string thumbprintSha256,
        string subject,
        string issuer,
        string serialNumber,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string certificatePem)
    {
        var now = DateTimeOffset.UtcNow;
        return new MachineClientCertificate
        {
            Id = Guid.NewGuid(),
            MachineClientId = machineClientId,
            DisplayName = displayName,
            ThumbprintSha256 = thumbprintSha256,
            Subject = subject,
            Issuer = issuer,
            SerialNumber = serialNumber,
            NotBefore = notBefore,
            ExpiresAt = expiresAt,
            CertificatePem = certificatePem,
            Status = MachineClientCertificateStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Revoke(string? reason)
    {
        if (this.Status == MachineClientCertificateStatus.Revoked)
        {
            return;
        }

        this.Status = MachineClientCertificateStatus.Revoked;
        this.RevokedAt = DateTimeOffset.UtcNow;
        this.RevocationReason = reason;
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsUsableAt(DateTimeOffset now)
    {
        return this.Status == MachineClientCertificateStatus.Active
            && this.NotBefore <= now
            && now <= this.ExpiresAt;
    }
}
