namespace Backend.Mcp.Api;

public sealed record IdmApiCallResult<T>(
    string Instance,
    string CorrelationId,
    T Value);
