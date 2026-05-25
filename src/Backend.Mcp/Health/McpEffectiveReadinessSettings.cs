namespace Backend.Mcp.Health;

public sealed record McpEffectiveReadinessSettings(
    string Profile,
    string Transport,
    bool RequiresCallerAuthentication,
    bool RequireDpop,
    bool AllowBearerTokensForDevelopment,
    string Audience,
    bool ReadOnly);
