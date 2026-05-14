using System.Text.Json.Serialization;

namespace Backend.Application.Models.Auth;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;
}
