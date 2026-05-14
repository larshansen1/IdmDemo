using System.Text.Json.Serialization;

namespace Backend.Application.Models.Auth;

public sealed class JwksResponse
{
    [JsonPropertyName("keys")]
    public IReadOnlyList<JsonWebKeyResponse> Keys { get; init; } = [];
}
