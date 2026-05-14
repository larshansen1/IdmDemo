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
        });

        modelBuilder.Entity<MachineClient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId).IsUnique();
            entity.Property(e => e.ClientId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).HasMaxLength(512);
        });
    }
}
