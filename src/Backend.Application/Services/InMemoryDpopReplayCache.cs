using System.Collections.Concurrent;

namespace Backend.Application.Services;

public sealed class InMemoryDpopReplayCache : IDpopReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

    public Task<bool> TryStoreAsync(
        string jwkThumbprint,
        string proofId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var now = DateTimeOffset.UtcNow;
        this.PruneExpired(now);
        var key = string.Create(null, $"{jwkThumbprint}:{proofId}");
        return Task.FromResult(this._entries.TryAdd(key, expiresAt));
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var entry in this._entries)
        {
            if (entry.Value <= now)
            {
                this._entries.TryRemove(entry.Key, out _);
            }
        }
    }
}
