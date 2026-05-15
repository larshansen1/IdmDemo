using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Application.Services;

public sealed class DpopProofValidator : IDpopProofValidator
{
    private const int _badRequestStatusCode = 400;
    private const int _allowedFutureSkewSeconds = 60;

    private readonly AuthorizationServerOptions _options;
    private readonly IDpopReplayCache _replayCache;

    public DpopProofValidator(AuthorizationServerOptions options, IDpopReplayCache replayCache)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replayCache);
        this._options = options;
        this._replayCache = replayCache;
    }

    public async Task<ValidatedDpopProof> ValidateTokenEndpointProofAsync(
        string proofJwt,
        Uri expectedTokenEndpoint,
        CancellationToken cancellationToken = default)
    {
        return await this.ValidateProofAsync(
            proofJwt,
            "POST",
            expectedTokenEndpoint,
            accessToken: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidatedDpopProof> ValidateProtectedResourceProofAsync(
        string proofJwt,
        string accessToken,
        string httpMethod,
        Uri expectedResourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);

        return await this.ValidateProofAsync(
            proofJwt,
            httpMethod,
            expectedResourceUri,
            accessToken,
            cancellationToken).ConfigureAwait(false);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal);
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(base64);
    }

    private static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var keyType = ReadRequiredString(jwk, "kty");
        string canonicalJson = keyType switch
        {
            "EC" => string.Create(
                null,
                $"{{\"crv\":\"{ReadRequiredString(jwk, "crv")}\",\"kty\":\"EC\",\"x\":\"{ReadRequiredString(jwk, "x")}\",\"y\":\"{ReadRequiredString(jwk, "y")}\"}}"),
            "RSA" => string.Create(
                null,
                $"{{\"e\":\"{ReadRequiredString(jwk, "e")}\",\"kty\":\"RSA\",\"n\":\"{ReadRequiredString(jwk, "n")}\"}}"),
            _ => throw CreateInvalidProofException(),
        };

        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    private static string ComputeAccessTokenHash(string accessToken)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
    }

    private static OAuthException CreateInvalidProofException()
    {
        return new OAuthException("invalid_dpop_proof", "DPoP proof is missing or invalid.", _badRequestStatusCode);
    }

    private static void ThrowInvalidProof()
    {
        throw CreateInvalidProofException();
    }

    private static JsonDocument ParseBase64UrlJson(string value)
    {
        try
        {
            return JsonDocument.Parse(Base64UrlDecode(value));
        }
        catch (ArgumentException)
        {
            ThrowInvalidProof();
            throw;
        }
        catch (JsonException)
        {
            ThrowInvalidProof();
            throw;
        }
        catch (FormatException)
        {
            ThrowInvalidProof();
            throw;
        }
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            ThrowInvalidProof();
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            ThrowInvalidProof();
        }

        return value ?? throw CreateInvalidProofException();
    }

    private static long ReadRequiredUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            ThrowInvalidProof();
        }

        if (!property.TryGetInt64(out var value))
        {
            ThrowInvalidProof();
        }

        return value;
    }

    private static void ValidateAlgorithm(string algorithm, IReadOnlyList<string> allowedAlgorithms)
    {
        if (string.Equals(algorithm, "none", StringComparison.Ordinal) ||
            algorithm.StartsWith("HS", StringComparison.Ordinal) ||
            !allowedAlgorithms.Contains(algorithm, StringComparer.Ordinal))
        {
            ThrowInvalidProof();
        }
    }

    private static void ValidateType(JsonElement header)
    {
        var type = ReadRequiredString(header, "typ");
        if (!string.Equals(type, "dpop+jwt", StringComparison.Ordinal))
        {
            ThrowInvalidProof();
        }
    }

    private static void ValidatePublicJwk(JsonElement jwk)
    {
        if (jwk.TryGetProperty("k", out _) ||
            jwk.TryGetProperty("d", out _) ||
            jwk.TryGetProperty("p", out _) ||
            jwk.TryGetProperty("q", out _) ||
            jwk.TryGetProperty("dp", out _) ||
            jwk.TryGetProperty("dq", out _) ||
            jwk.TryGetProperty("qi", out _) ||
            jwk.TryGetProperty("oth", out _))
        {
            ThrowInvalidProof();
        }
    }

    [SuppressMessage(
        "Security",
        "CA5404:Do not disable token validation checks",
        Justification = "DPoP proofs do not contain issuer, audience, or JWT lifetime claims. Those DPoP-specific claims are validated separately.")]
    private static void ValidateSignature(
        string proofJwt,
        string jwkJson,
        IReadOnlyList<string> allowedAlgorithms)
    {
        var handler = new JsonWebTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            RequireSignedTokens = true,
            IssuerSigningKey = new JsonWebKey(jwkJson),
            ValidAlgorithms = allowedAlgorithms,
        };

        var result = handler.ValidateTokenAsync(proofJwt, validationParameters).GetAwaiter().GetResult();
        if (!result.IsValid)
        {
            ThrowInvalidProof();
        }
    }

    private static void ValidateProofClaims(
        JsonElement payload,
        string expectedHttpMethod,
        Uri expectedUri,
        string? accessToken,
        int proofLifetimeSeconds)
    {
        var httpMethod = ReadRequiredString(payload, "htm");
        if (!string.Equals(httpMethod, expectedHttpMethod, StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidProof();
        }

        var httpUri = ReadRequiredString(payload, "htu");
        if (!Uri.TryCreate(httpUri, UriKind.Absolute, out var parsedHttpUri) ||
            !UriMatches(parsedHttpUri, expectedUri))
        {
            ThrowInvalidProof();
        }

        if (accessToken is not null)
        {
            var accessTokenHash = ReadRequiredString(payload, "ath");
            if (!string.Equals(accessTokenHash, ComputeAccessTokenHash(accessToken), StringComparison.Ordinal))
            {
                ThrowInvalidProof();
            }
        }

        _ = ReadRequiredString(payload, "jti");
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(ReadRequiredUnixTime(payload, "iat"));
        var now = DateTimeOffset.UtcNow;
        if (issuedAt < now.AddSeconds(-proofLifetimeSeconds) || issuedAt > now.AddSeconds(_allowedFutureSkewSeconds))
        {
            ThrowInvalidProof();
        }
    }

    private static bool UriMatches(Uri actual, Uri expected)
    {
        return string.Equals(actual.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase) &&
            actual.Port == expected.Port &&
            string.Equals(actual.AbsolutePath, expected.AbsolutePath, StringComparison.Ordinal);
    }

    private async Task<ValidatedDpopProof> ValidateProofAsync(
        string proofJwt,
        string expectedHttpMethod,
        Uri expectedUri,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedUri);

        if (string.IsNullOrWhiteSpace(proofJwt))
        {
            ThrowInvalidProof();
        }

        var parts = proofJwt.Split('.');
        if (parts.Length != 3)
        {
            ThrowInvalidProof();
        }

        using var headerDocument = ParseBase64UrlJson(parts[0]);
        using var payloadDocument = ParseBase64UrlJson(parts[1]);
        var header = headerDocument.RootElement;
        var payload = payloadDocument.RootElement;

        var algorithm = ReadRequiredString(header, "alg");
        ValidateAlgorithm(algorithm, this._options.DpopSupportedAlgorithms);
        ValidateType(header);

        if (!header.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalidProof();
        }

        ValidatePublicJwk(jwk);
        var jwkJson = jwk.GetRawText();
        var jwkThumbprint = ComputeJwkThumbprint(jwk);
        ValidateSignature(proofJwt, jwkJson, this._options.DpopSupportedAlgorithms);

        ValidateProofClaims(
            payload,
            expectedHttpMethod,
            expectedUri,
            accessToken,
            this._options.DpopProofLifetimeSeconds);
        var proofId = ReadRequiredString(payload, "jti");
        var replayExpiresAt = DateTimeOffset.UtcNow.AddSeconds(this._options.DpopReplayCacheSeconds);
        var stored = await this._replayCache
            .TryStoreAsync(jwkThumbprint, proofId, replayExpiresAt, cancellationToken)
            .ConfigureAwait(false);

        if (!stored)
        {
            ThrowInvalidProof();
        }

        return new ValidatedDpopProof
        {
            JwkThumbprint = jwkThumbprint,
        };
    }
}
