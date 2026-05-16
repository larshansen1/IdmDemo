namespace Backend.Mcp.Tools;

public sealed record OperationResult(
    string Instance,
    string CorrelationId,
    string Status);
