using System.Text.Json.Serialization;

namespace Backend.Application.Models.Auth;

public sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; init; } = string.Empty;
}
