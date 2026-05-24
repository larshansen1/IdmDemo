namespace Backend.Mcp.Health;

public sealed record McpReadinessReport(
    string Status,
    string Profile,
    string Transport,
    bool RequiresCallerAuthentication,
    bool RequireDpop,
    bool AllowBearerTokensForDevelopment,
    string Audience,
    bool ReadOnly,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Errors);
