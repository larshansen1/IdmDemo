namespace Backend.Application.Models.Auth;

public sealed class AuthorizationServerOptions
{
    public string Issuer { get; init; } = "https://localhost:5001";

    public string Audience { get; init; } = "idm-demo-api";

    public int AccessTokenLifetimeSeconds { get; init; } = 3600;
}
