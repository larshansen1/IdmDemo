using System.Security.Cryptography;
using System.Text;
using Backend.Application.Services;
using Backend.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Services;

public sealed class PersistentDpopReplayCache : IDpopReplayCache
{
    private const int _sqliteConstraintViolation = 19;

    private readonly AppDbContext _db;

    public PersistentDpopReplayCache(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        this._db = db;
    }

    public async Task<bool> TryStoreAsync(
        string jwkThumbprint,
        string proofId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await this._db.DpopReplayEntries
            .Where(entry => entry.ExpiresAtUnixTimeSeconds <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        this._db.DpopReplayEntries.Add(new DpopReplayEntry
        {
            Key = CreateKey(jwkThumbprint, proofId),
            ExpiresAtUnixTimeSeconds = expiresAt.ToUnixTimeSeconds(),
        });

        try
        {
            await this._db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: _sqliteConstraintViolation })
        {
            this._db.ChangeTracker.Clear();
            return false;
        }
    }

    private static string CreateKey(string jwkThumbprint, string proofId)
    {
        var keyMaterial = string.Create(null, $"{jwkThumbprint}\0{proofId}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
    }
}
