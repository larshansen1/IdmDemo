using System.Text.Json.Serialization;

namespace Backend.Application.Models.Auth;

public sealed class JsonWebKeyResponse
{
    [JsonPropertyName("kty")]
    public string KeyType { get; init; } = "RSA";

    [JsonPropertyName("use")]
    public string PublicKeyUse { get; init; } = "sig";

    [JsonPropertyName("kid")]
    public string KeyId { get; init; } = string.Empty;

    [JsonPropertyName("alg")]
    public string Algorithm { get; init; } = "RS256";

    [JsonPropertyName("n")]
    public string Modulus { get; init; } = string.Empty;

    [JsonPropertyName("e")]
    public string Exponent { get; init; } = string.Empty;
}
