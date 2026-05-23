namespace Backend.Mcp.Health;

public sealed record McpReadinessReport(
    string Status,
    string Transport,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Errors);
