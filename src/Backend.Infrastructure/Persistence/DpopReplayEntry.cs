namespace Backend.Infrastructure.Persistence;

public sealed class DpopReplayEntry
{
    public string Key { get; set; } = string.Empty;

    public long ExpiresAtUnixTimeSeconds { get; set; }
}
