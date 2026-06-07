using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.As.Domain;
using Backend.As.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class AuthorizationServerService : IAuthorizationServerService
{
    private const int _badRequestStatusCode = 400;

    private readonly AuthorizationServerOptions _options;
    private readonly IIssuanceContextProvider _issuanceContextProvider;
    private readonly IJwtSigningKeyStore _signingKeyStore;
    private readonly IDpopProofValidator _dpopProofValidator;
    private readonly ILogger<AuthorizationServerService> _logger;

    public AuthorizationServerService(
        AuthorizationServerOptions options,
        IIssuanceContextProvider issuanceContextProvider,
        IJwtSigningKeyStore signingKeyStore,
        IDpopProofValidator dpopProofValidator,
        ILogger<AuthorizationServerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options;
        this._issuanceContextProvider = issuanceContextProvider;
        this._signingKeyStore = signingKeyStore;
        this._dpopProofValidator = dpopProofValidator;
        this._logger = logger;
    }

    public DiscoveryResponse GetDiscovery()
    {
        return new DiscoveryResponse
        {
            Issuer = this._options.Issuer,
            TokenEndpoint = new Uri($"{this._options.Issuer}/connect/token"),
            JwksUri = new Uri($"{this._options.Issuer}/.well-known/jwks.json"),
            GrantTypesSupported = ["client_credentials"],
            TokenEndpointAuthMethodsSupported = ["self_signed_tls_client_auth"],
            TlsClientCertificateBoundAccessTokens = true,
            DpopSigningAlgValuesSupported = this._options.DpopSupportedAlgorithms,
        };
    }

    public async Task<JwksResponse> GetJwksAsync(CancellationToken cancellationToken = default)
    {
        var key = await this._signingKeyStore.GetActiveKeyAsync(cancellationToken).ConfigureAwait(false);
        return new JwksResponse
        {
            Keys =
            [
                new JsonWebKeyResponse
                {
                    KeyId = key.KeyId,
                    Modulus = Base64UrlEncode(key.Parameters.Modulus ?? []),
                    Exponent = Base64UrlEncode(key.Parameters.Exponent ?? []),
                },
            ],
        };
    }

    public async Task<TokenResponse> IssueClientCredentialsTokenAsync(
        string? grantType,
        string? clientId,
        string? scope,
        X509Certificate2? clientCertificate,
        string? dpopProofJwt = null,
        string? resource = null,
        CancellationToken cancellationToken = default)
    {
        ValidateGrantType(grantType);
        var dpopProof = await this.ValidateDpopProofAsync(dpopProofJwt, cancellationToken).ConfigureAwait(false);
        var context = await this._issuanceContextProvider
            .ResolveAsync(clientId, clientCertificate, cancellationToken)
            .ConfigureAwait(false);
        var grantedScopes = ResolveGrantedScopes(scope, context.ActiveScopes);
        var audience = this.ResolveAudience(resource, context.ActiveScopes);
        var token = await this.CreateJwtAsync(
            context,
            audience,
            grantedScopes,
            dpopProof?.JwkThumbprint,
            cancellationToken).ConfigureAwait(false);

        LogTokenIssued(this._logger, context.ClientRecordId, context.ClientId);

        return new TokenResponse
        {
            AccessToken = token,
            TokenType = dpopProof is null ? "Bearer" : "DPoP",
            ExpiresIn = this._options.AccessTokenLifetimeSeconds,
            Scope = string.Join(' ', grantedScopes),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AccessTokenIssued {ClientRecordId} {ClientId}")]
    private static partial void LogTokenIssued(ILogger logger, Guid clientRecordId, string clientId);

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private static string ComputeCertificateThumbprintBase64Url(X509Certificate2 certificate)
    {
        return Base64UrlEncode(SHA256.HashData(certificate.RawData));
    }

    private static IReadOnlyList<string> ResolveGrantedScopes(
        string? requestedScope,
        IReadOnlyList<string> assignedScopes)
    {
        if (string.IsNullOrWhiteSpace(requestedScope))
        {
            return assignedScopes;
        }

        var requestedScopes = requestedScope.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var invalidScopes = requestedScopes.Except(assignedScopes, StringComparer.Ordinal).ToList();
        if (invalidScopes.Count > 0)
        {
            throw new OAuthException("invalid_scope", "Requested scope is not assigned to the client.", _badRequestStatusCode);
        }

        return requestedScopes;
    }

    private static void ValidateGrantType(string? grantType)
    {
        if (string.IsNullOrWhiteSpace(grantType))
        {
            throw new OAuthException("invalid_request", "grant_type is required.", _badRequestStatusCode);
        }

        if (!string.Equals(grantType, "client_credentials", StringComparison.Ordinal))
        {
            throw new OAuthException("unsupported_grant_type", "Only client_credentials is supported.", _badRequestStatusCode);
        }
    }

    private static string CreateEncodedHeader(JwtSigningKey key)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "at+jwt",
            ["kid"] = key.KeyId,
        };

        return Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
    }

    private static string CreateEncodedPayload(
        AuthorizationServerOptions options,
        IssuanceContext context,
        string audience,
        IReadOnlyList<string> grantedScopes,
        string? dpopJwkThumbprint,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var confirmation = string.IsNullOrWhiteSpace(dpopJwkThumbprint)
            ? new Dictionary<string, object>
            {
                ["x5t#S256"] = ComputeCertificateThumbprintBase64Url(context.Certificate),
            }
            : new Dictionary<string, object>
            {
                ["jkt"] = dpopJwkThumbprint,
            };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = options.Issuer,
            ["sub"] = context.ClientRecordId.ToString(),
            ["client_id"] = context.ClientId,
            ["aud"] = audience,
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["scope"] = string.Join(' ', grantedScopes),
            ["roles"] = context.ActiveRoles,
            ["cnf"] = confirmation,
        };

        return Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    private static string Sign(string signingInput, JwtSigningKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(key.Parameters);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Base64UrlEncode(signature);
    }

    private async Task<ValidatedDpopProof?> ValidateDpopProofAsync(
        string? dpopProofJwt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dpopProofJwt))
        {
            if (this._options.RequireDpop)
            {
                throw new OAuthException("invalid_dpop_proof", "DPoP proof is missing or invalid.", _badRequestStatusCode);
            }

            return null;
        }

        return await this._dpopProofValidator
            .ValidateTokenEndpointProofAsync(dpopProofJwt, new Uri($"{this._options.Issuer}/connect/token"), cancellationToken)
            .ConfigureAwait(false);
    }

    private string ResolveAudience(string? requestedResource, IReadOnlyList<string> activeAssignedScopes)
    {
        if (string.IsNullOrWhiteSpace(requestedResource))
        {
            return this._options.Audience;
        }

        var resource = requestedResource.Trim();
        if (string.Equals(resource, this._options.Audience, StringComparison.Ordinal))
        {
            return this._options.Audience;
        }

        var hasMcpScope = activeAssignedScopes
            .Any(scope => scope.StartsWith(this._options.McpScopePrefix, StringComparison.Ordinal));
        if (string.Equals(resource, this._options.McpAudience, StringComparison.Ordinal) && hasMcpScope)
        {
            return this._options.McpAudience;
        }

        throw new OAuthException(
            "invalid_target",
            "Requested resource is not allowed for this client.",
            _badRequestStatusCode);
    }

    private async Task<string> CreateJwtAsync(
        IssuanceContext context,
        string audience,
        IReadOnlyList<string> grantedScopes,
        string? dpopJwkThumbprint,
        CancellationToken cancellationToken)
    {
        var key = await this._signingKeyStore.GetActiveKeyAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddSeconds(this._options.AccessTokenLifetimeSeconds);
        var encodedHeader = CreateEncodedHeader(key);
        var encodedPayload = CreateEncodedPayload(
            this._options,
            context,
            audience,
            grantedScopes,
            dpopJwkThumbprint,
            now,
            expiresAt);
        var signingInput = string.Create(
            CultureInfo.InvariantCulture,
            $"{encodedHeader}.{encodedPayload}");
        var signature = Sign(signingInput, key);

        return string.Create(CultureInfo.InvariantCulture, $"{signingInput}.{signature}");
    }
}
