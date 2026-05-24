namespace Backend.Mcp;

public sealed record McpEffectiveRuntimeSettings(
    McpProfile Profile,
    McpTransport Transport,
    bool RequiresCallerAuthentication,
    bool RequireDpop,
    bool AllowBearerTokensForDevelopment,
    string Audience,
    bool ReadOnly);
