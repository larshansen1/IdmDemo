using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Backend.Mcp.Api;

public static class DpopProofFactory
{
    public static string Create(RSA key, string httpMethod, Uri httpUri, string? accessToken = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(httpMethod);
        ArgumentNullException.ThrowIfNull(httpUri);

        var parameters = key.ExportParameters(includePrivateParameters: false);
        var n = Base64UrlEncode(parameters.Modulus!);
        var e = Base64UrlEncode(parameters.Exponent!);

        var headerJson = JsonSerializer.Serialize(new
        {
            typ = "dpop+jwt",
            alg = "RS256",
            jwk = new { kty = "RSA", n, e },
        });

        // RFC 9449: htu must not include query or fragment
        var htu = new UriBuilder(httpUri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;

        var jti = Guid.NewGuid().ToString("D");
        var htm = httpMethod.ToUpperInvariant();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payloadJson = accessToken is not null
            ? JsonSerializer.Serialize(new
            {
                jti,
                htm,
                htu,
                iat,
                ath = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken))),
            })
            : JsonSerializer.Serialize(new { jti, htm, htu, iat });

        var header = Base64UrlEncodeUtf8(headerJson);
        var payload = Base64UrlEncodeUtf8(payloadJson);
        var signingInput = $"{header}.{payload}";

        var signature = Base64UrlEncode(
            key.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        return $"{signingInput}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);

    private static string Base64UrlEncodeUtf8(string json) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(json));
}
