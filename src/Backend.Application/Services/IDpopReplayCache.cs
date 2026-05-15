namespace Backend.Application.Services;

public interface IDpopReplayCache
{
    Task<bool> TryStoreAsync(
        string jwkThumbprint,
        string proofId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
