namespace Backend.Application.Models.Auth;

public sealed class AuthorizationServerOptions
{
    public string Issuer { get; init; } = "https://localhost:5001";

    public string Audience { get; init; } = "idm-demo-api";

    public int AccessTokenLifetimeSeconds { get; init; } = 3600;

    public bool RequireDpop { get; init; }

    public int DpopProofLifetimeSeconds { get; init; } = 300;

    public int DpopReplayCacheSeconds { get; init; } = 300;

    public IReadOnlyList<string> DpopSupportedAlgorithms { get; init; } = ["ES256", "RS256"];
}
