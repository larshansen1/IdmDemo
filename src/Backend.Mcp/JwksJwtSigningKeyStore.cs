using System.Security.Cryptography;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.As.Domain.Services;
using Backend.Mcp.Api;

namespace Backend.Mcp;

public sealed class JwksJwtSigningKeyStore : IJwtSigningKeyStore
{
    private const int _unauthorizedStatusCode = 401;

    private readonly IIdmApiClient _idmApiClient;

    public JwksJwtSigningKeyStore(IIdmApiClient idmApiClient)
    {
        ArgumentNullException.ThrowIfNull(idmApiClient);

        this._idmApiClient = idmApiClient;
    }

    public async Task<JwtSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
    {
        var jwks = await this._idmApiClient.GetJwksAsync(null, cancellationToken).ConfigureAwait(false);
        var key = jwks.Value.Keys.FirstOrDefault(IsSupportedSigningKey);
        if (key is null)
        {
            throw CreateInvalidTokenException();
        }

        return new JwtSigningKey
        {
            KeyId = key.KeyId,
            Parameters = new RSAParameters
            {
                Modulus = Base64UrlDecode(key.Modulus),
                Exponent = Base64UrlDecode(key.Exponent),
            },
        };
    }

    private static bool IsSupportedSigningKey(JsonWebKeyResponse key)
    {
        return string.Equals(key.KeyType, "RSA", StringComparison.Ordinal) &&
            string.Equals(key.PublicKeyUse, "sig", StringComparison.Ordinal) &&
            string.Equals(key.Algorithm, "RS256", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(key.KeyId) &&
            !string.IsNullOrWhiteSpace(key.Modulus) &&
            !string.IsNullOrWhiteSpace(key.Exponent);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');
        var paddingLength = (4 - (padded.Length % 4)) % 4;
        padded = padded.PadRight(padded.Length + paddingLength, '=');

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw CreateInvalidTokenException();
        }
    }

    private static OAuthException CreateInvalidTokenException()
    {
        return new OAuthException("invalid_token", "Access token is missing or invalid.", _unauthorizedStatusCode);
    }
}
