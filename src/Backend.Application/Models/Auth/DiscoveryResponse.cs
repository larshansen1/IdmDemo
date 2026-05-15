using System.Text.Json.Serialization;

namespace Backend.Application.Models.Auth;

public sealed class DiscoveryResponse
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public Uri TokenEndpoint { get; init; } = new("about:blank");

    [JsonPropertyName("jwks_uri")]
    public Uri JwksUri { get; init; } = new("about:blank");

    [JsonPropertyName("grant_types_supported")]
    public IReadOnlyList<string> GrantTypesSupported { get; init; } = [];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; init; } = [];

    [JsonPropertyName("tls_client_certificate_bound_access_tokens")]
    public bool TlsClientCertificateBoundAccessTokens { get; init; }

    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public IReadOnlyList<string> DpopSigningAlgValuesSupported { get; init; } = [];
}
