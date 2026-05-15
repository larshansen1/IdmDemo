using Backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<MachineClient> MachineClients { get; set; } = null!;

    public DbSet<GlobalRole> GlobalRoles { get; set; } = null!;

    public DbSet<GlobalScope> GlobalScopes { get; set; } = null!;

    public DbSet<MachineClientCertificate> MachineClientCertificates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).HasMaxLength(512);
            entity.Property(e => e.ExternalId).HasMaxLength(256);
            entity.Property(e => e.AssignedRoleValues).HasMaxLength(2048);
        });

        modelBuilder.Entity<MachineClient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId).IsUnique();
            entity.Property(e => e.ClientId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).HasMaxLength(512);
            entity.Property(e => e.CertificateThumbprintSha256).HasMaxLength(64);
            entity.Property(e => e.CertificateSubject).HasMaxLength(512);
            entity.Property(e => e.AssignedScopeValues).HasMaxLength(2048);
            entity.Property(e => e.AssignedRoleValues).HasMaxLength(2048);
        });

        modelBuilder.Entity<GlobalRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Value).IsUnique();
            entity.Property(e => e.Value).IsRequired().HasMaxLength(128);
            entity.Property(e => e.DisplayName).HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<GlobalScope>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Value).IsUnique();
            entity.Property(e => e.Value).IsRequired().HasMaxLength(128);
            entity.Property(e => e.DisplayName).HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<MachineClientCertificate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ThumbprintSha256).IsUnique();
            entity.HasIndex(e => new { e.MachineClientId, e.ThumbprintSha256 });
            entity.Property(e => e.DisplayName).HasMaxLength(512);
            entity.Property(e => e.ThumbprintSha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Issuer).IsRequired().HasMaxLength(512);
            entity.Property(e => e.SerialNumber).IsRequired().HasMaxLength(128);
            entity.Property(e => e.CertificatePem).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.RevocationReason).HasMaxLength(512);
            entity.HasOne<MachineClient>()
                .WithMany()
                .HasForeignKey(e => e.MachineClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
