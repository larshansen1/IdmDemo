namespace Backend.Mcp.Health;

public sealed record McpRawReadinessSettings(
    string? Profile,
    string? Transport,
    bool? RequireDpop,
    bool? AllowBearerTokensForDevelopment,
    string? Audience,
    bool? ReadOnly);
