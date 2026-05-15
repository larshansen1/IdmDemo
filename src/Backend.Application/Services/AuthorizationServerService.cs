using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Domain.Entities;
using Backend.Domain.Repositories;
using Backend.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class AuthorizationServerService : IAuthorizationServerService
{
    private const int _badRequestStatusCode = 400;
    private const int _unauthorizedStatusCode = 401;

    private readonly AuthorizationServerOptions _options;
    private readonly IMachineClientRepository _clientRepository;
    private readonly IMachineClientCertificateRepository _certificateRepository;
    private readonly IJwtSigningKeyStore _signingKeyStore;
    private readonly IDpopProofValidator _dpopProofValidator;
    private readonly ILogger<AuthorizationServerService> _logger;

    public AuthorizationServerService(
        AuthorizationServerOptions options,
        IMachineClientRepository clientRepository,
        IMachineClientCertificateRepository certificateRepository,
        IJwtSigningKeyStore signingKeyStore,
        IDpopProofValidator dpopProofValidator,
        ILogger<AuthorizationServerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options;
        this._clientRepository = clientRepository;
        this._certificateRepository = certificateRepository;
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
        CancellationToken cancellationToken = default)
    {
        ValidateGrantType(grantType);
        var client = await this.AuthenticateClientAsync(clientId, clientCertificate, cancellationToken).ConfigureAwait(false);
        var dpopProof = await this.ValidateDpopProofAsync(dpopProofJwt, cancellationToken).ConfigureAwait(false);
        var grantedScopes = ResolveGrantedScopes(scope, client);
        var token = await this.CreateJwtAsync(
            client,
            clientCertificate!,
            grantedScopes,
            dpopProof?.JwkThumbprint,
            cancellationToken).ConfigureAwait(false);

        LogTokenIssued(this._logger, client.Id, client.ClientId);

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

    private static string ComputeCertificateThumbprintHex(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    private static string ComputeCertificateThumbprintBase64Url(X509Certificate2 certificate)
    {
        return Base64UrlEncode(SHA256.HashData(certificate.RawData));
    }

    private static IReadOnlyList<string> ResolveGrantedScopes(string? requestedScope, MachineClient client)
    {
        var assignedScopes = client.GetAssignedScopes();
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

    private static void ValidateClientCertificate(MachineClient client, X509Certificate2 certificate)
    {
        if (string.IsNullOrWhiteSpace(client.CertificateThumbprintSha256))
        {
            throw new OAuthException("invalid_client", "Client certificate is not registered.", _unauthorizedStatusCode);
        }

        if (DateTimeOffset.UtcNow > certificate.NotAfter)
        {
            throw new OAuthException("invalid_client", "Client certificate is expired.", _unauthorizedStatusCode);
        }

        if (client.CertificateExpiresAt is not null && DateTimeOffset.UtcNow > client.CertificateExpiresAt)
        {
            throw new OAuthException("invalid_client", "Registered client certificate is expired.", _unauthorizedStatusCode);
        }

        var actualThumbprint = ComputeCertificateThumbprintHex(certificate);
        if (!string.Equals(actualThumbprint, client.CertificateThumbprintSha256, StringComparison.Ordinal))
        {
            throw new OAuthException("invalid_client", "Client certificate does not match registration.", _unauthorizedStatusCode);
        }
    }

    private static void ValidateRegisteredCertificate(MachineClientCertificate certificate)
    {
        if (certificate.Status == MachineClientCertificateStatus.Revoked)
        {
            throw new OAuthException("invalid_client", "Client certificate is revoked.", _unauthorizedStatusCode);
        }

        if (!certificate.IsUsableAt(DateTimeOffset.UtcNow))
        {
            throw new OAuthException("invalid_client", "Client certificate is expired.", _unauthorizedStatusCode);
        }
    }

    private static string CreateEncodedHeader(JwtSigningKey key)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = key.KeyId,
        };

        return Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
    }

    private static string CreateEncodedPayload(
        AuthorizationServerOptions options,
        MachineClient client,
        X509Certificate2 certificate,
        IReadOnlyList<string> grantedScopes,
        string? dpopJwkThumbprint,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var confirmation = string.IsNullOrWhiteSpace(dpopJwkThumbprint)
            ? new Dictionary<string, object>
            {
                ["x5t#S256"] = ComputeCertificateThumbprintBase64Url(certificate),
            }
            : new Dictionary<string, object>
            {
                ["jkt"] = dpopJwkThumbprint,
            };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = options.Issuer,
            ["sub"] = client.Id.ToString(),
            ["client_id"] = client.ClientId,
            ["aud"] = options.Audience,
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["scope"] = string.Join(' ', grantedScopes),
            ["roles"] = client.GetAssignedRoles(),
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

    private async Task<MachineClient> AuthenticateClientAsync(
        string? clientId,
        X509Certificate2? clientCertificate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new OAuthException("invalid_request", "client_id is required.", _badRequestStatusCode);
        }

        if (clientCertificate is null)
        {
            throw new OAuthException("invalid_client", "Client certificate is missing or invalid.", _unauthorizedStatusCode);
        }

        var client = await this._clientRepository.GetByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (client is null || !client.Active)
        {
            throw new OAuthException("invalid_client", "Client authentication failed.", _unauthorizedStatusCode);
        }

        await this.ValidateClientCertificateAsync(client, clientCertificate, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private async Task ValidateClientCertificateAsync(
        MachineClient client,
        X509Certificate2 clientCertificate,
        CancellationToken cancellationToken)
    {
        var actualThumbprint = ComputeCertificateThumbprintHex(clientCertificate);
        var registeredCertificate = await this._certificateRepository
            .GetByThumbprintAsync(client.Id, actualThumbprint, cancellationToken)
            .ConfigureAwait(false);

        if (registeredCertificate is not null)
        {
            ValidateRegisteredCertificate(registeredCertificate);
            return;
        }

        ValidateClientCertificate(client, clientCertificate);
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

    private async Task<string> CreateJwtAsync(
        MachineClient client,
        X509Certificate2 certificate,
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
            client,
            certificate,
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
