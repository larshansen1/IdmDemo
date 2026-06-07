using System.Security.Cryptography;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.As.Domain.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Application.Services;

public sealed class AccessTokenValidator : IAccessTokenValidator
{
    private const int _unauthorizedStatusCode = 401;

    private readonly AuthorizationServerOptions _options;
    private readonly IJwtSigningKeyStore _signingKeyStore;

    public AccessTokenValidator(AuthorizationServerOptions options, IJwtSigningKeyStore signingKeyStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signingKeyStore);
        this._options = options;
        this._signingKeyStore = signingKeyStore;
    }

    public async Task<ValidatedAccessToken> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            ThrowInvalidToken();
        }

        var key = await this._signingKeyStore.GetActiveKeyAsync(cancellationToken).ConfigureAwait(false);
        var token = await ValidateJwtAsync(accessToken, key, this._options).ConfigureAwait(false);
        var confirmation = ReadConfirmationClaims(token);

        return new ValidatedAccessToken
        {
            Subject = ReadRequiredClaim(token, JwtRegisteredClaimNames.Sub),
            ClientId = ReadRequiredClaim(token, "client_id"),
            Scope = ReadOptionalClaim(token, "scope") ?? string.Empty,
            Roles = ReadRoles(token),
            DpopJwkThumbprint = confirmation.DpopJwkThumbprint,
            CertificateThumbprintSha256 = confirmation.CertificateThumbprintSha256,
        };
    }

    private static List<string> ReadRoles(JsonWebToken token)
    {
        return token.Claims
            .Where(c => string.Equals(c.Type, "roles", StringComparison.Ordinal))
            .Select(c => c.Value)
            .ToList();
    }

    private static string? ReadOptionalClaim(JsonWebToken token, string claimType)
    {
        return token.Claims.FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))?.Value;
    }

    private static string ReadRequiredClaim(JsonWebToken token, string claimType)
    {
        var value = ReadOptionalClaim(token, claimType);
        if (string.IsNullOrWhiteSpace(value))
        {
            ThrowInvalidToken();
        }

        return value ?? throw CreateInvalidTokenException();
    }

    private static (string? DpopJwkThumbprint, string? CertificateThumbprintSha256) ReadConfirmationClaims(JsonWebToken token)
    {
        var confirmationJson = ReadOptionalClaim(token, "cnf");
        if (string.IsNullOrWhiteSpace(confirmationJson))
        {
            ThrowInvalidToken();
        }

        try
        {
            using var document = JsonDocument.Parse(confirmationJson!);
            var root = document.RootElement;
            var jkt = root.TryGetProperty("jkt", out var jktProperty) &&
                jktProperty.ValueKind == JsonValueKind.String
                    ? jktProperty.GetString()
                    : null;
            var certificateThumbprint = root.TryGetProperty("x5t#S256", out var certificateProperty) &&
                certificateProperty.ValueKind == JsonValueKind.String
                    ? certificateProperty.GetString()
                    : null;

            return (jkt, certificateThumbprint);
        }
        catch (JsonException)
        {
            ThrowInvalidToken();
            throw;
        }
    }

    private static OAuthException CreateInvalidTokenException()
    {
        return new OAuthException("invalid_token", "Access token is missing or invalid.", _unauthorizedStatusCode);
    }

    private static void ThrowInvalidToken()
    {
        throw CreateInvalidTokenException();
    }

    private static async Task<JsonWebToken> ValidateJwtAsync(
        string accessToken,
        JwtSigningKey key,
        AuthorizationServerOptions options)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(key.Parameters);
        var securityKey = new RsaSecurityKey(rsa.ExportParameters(false))
        {
            KeyId = key.KeyId,
        };
        var validationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateTokenReplay = false,
            ValidTypes = ["at+jwt"],
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(accessToken, validationParameters).ConfigureAwait(false);
        if (!result.IsValid)
        {
            ThrowInvalidToken();
        }

        return result.SecurityToken as JsonWebToken ?? throw CreateInvalidTokenException();
    }
}
