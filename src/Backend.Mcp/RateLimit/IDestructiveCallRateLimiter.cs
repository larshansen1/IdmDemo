namespace Backend.Mcp.RateLimit;

public interface IDestructiveCallRateLimiter
{
    void RecordAndEnforce(string callerKey);
}
