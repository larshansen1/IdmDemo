using Backend.Api.Services;
using Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.IntegrationTests.Auth;

public sealed class PersistentDpopReplayCacheTests
{
    [Fact]
    public async Task TryStoreAsync_ReplayedProofAcrossDbContexts_ReturnsFalse()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"idm_dpop_replay_{Guid.NewGuid():N}.db");
        try
        {
            await using (var setupDb = CreateDbContext(dbPath))
            {
                await setupDb.Database.MigrateAsync();
            }

            await using (var firstDb = CreateDbContext(dbPath))
            {
                var firstCache = new PersistentDpopReplayCache(firstDb);
                var stored = await firstCache.TryStoreAsync(
                    "test-thumbprint",
                    "proof-id",
                    DateTimeOffset.UtcNow.AddMinutes(5));

                Assert.True(stored);
            }

            await using (var secondDb = CreateDbContext(dbPath))
            {
                var secondCache = new PersistentDpopReplayCache(secondDb);
                var stored = await secondCache.TryStoreAsync(
                    "test-thumbprint",
                    "proof-id",
                    DateTimeOffset.UtcNow.AddMinutes(5));

                Assert.False(stored);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static AppDbContext CreateDbContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new AppDbContext(options);
    }
}
