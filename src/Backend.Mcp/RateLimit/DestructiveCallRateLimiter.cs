using System.Collections.Concurrent;

namespace Backend.Mcp.RateLimit;

public sealed class DestructiveCallRateLimiter : IDestructiveCallRateLimiter
{
    public const int Limit = 3;

    private static readonly TimeSpan _window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, SlidingWindow> _windows =
        new(StringComparer.Ordinal);

    public void RecordAndEnforce(string callerKey)
    {
        this._windows.GetOrAdd(callerKey, _ => new SlidingWindow()).RecordAndEnforce();
    }

    private sealed class SlidingWindow
    {
        private readonly Queue<DateTimeOffset> _timestamps = new();
        private readonly Lock _lock = new();

        public void RecordAndEnforce()
        {
            var now = DateTimeOffset.UtcNow;
            lock (this._lock)
            {
                var cutoff = now - _window;
                while (this._timestamps.Count > 0 && this._timestamps.Peek() < cutoff)
                {
                    this._timestamps.Dequeue();
                }

                if (this._timestamps.Count >= Limit)
                {
                    throw new McpToolException(
                        $"Rate limit exceeded: at most {Limit} destructive operations per hour per caller.");
                }

                this._timestamps.Enqueue(now);
            }
        }
    }
}
