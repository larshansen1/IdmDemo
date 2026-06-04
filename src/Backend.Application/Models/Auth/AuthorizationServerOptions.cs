namespace Backend.Application.Models.Auth;

public sealed class AuthorizationServerOptions
{
    public string Issuer { get; init; } = "https://localhost:5001";

    public string Audience { get; init; } = "idm-demo-api";

    public string McpAudience { get; init; } = "idm-demo-mcp";

    public string McpScopePrefix { get; init; } = "idm.mcp.";

    public int AccessTokenLifetimeSeconds { get; init; } = 300;

    public bool RequireDpop { get; init; } = true;

    public int DpopProofLifetimeSeconds { get; init; } = 300;

    public int DpopReplayCacheSeconds { get; init; } = 300;

    public IReadOnlyList<string> DpopSupportedAlgorithms { get; init; } = ["ES256", "RS256"];
}
