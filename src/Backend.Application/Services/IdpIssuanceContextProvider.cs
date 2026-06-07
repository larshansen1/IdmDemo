using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Auth;
using Backend.As.Domain;
using Backend.Idp.Domain.Entities;
using Backend.Idp.Domain.Repositories;

namespace Backend.Application.Services;

public sealed class IdpIssuanceContextProvider : IIssuanceContextProvider
{
    private const int _badRequestStatusCode = 400;
    private const int _unauthorizedStatusCode = 401;

    private readonly IMachineClientRepository _clientRepository;
    private readonly IMachineClientCertificateRepository _certificateRepository;
    private readonly IGlobalScopeRepository _scopeRepository;
    private readonly IGlobalRoleRepository _roleRepository;

    public IdpIssuanceContextProvider(
        IMachineClientRepository clientRepository,
        IMachineClientCertificateRepository certificateRepository,
        IGlobalScopeRepository scopeRepository,
        IGlobalRoleRepository roleRepository)
    {
        this._clientRepository = clientRepository;
        this._certificateRepository = certificateRepository;
        this._scopeRepository = scopeRepository;
        this._roleRepository = roleRepository;
    }

    public async Task<IssuanceContext> ResolveAsync(
        string? clientId,
        X509Certificate2? certificate,
        CancellationToken cancellationToken)
    {
        var client = await this.AuthenticateClientAsync(clientId, certificate, cancellationToken).ConfigureAwait(false);
        var activeAssignedScopes = await this.ResolveActiveAssignedScopesAsync(client, cancellationToken).ConfigureAwait(false);
        var activeAssignedRoles = await this.ResolveActiveAssignedRolesAsync(client, cancellationToken).ConfigureAwait(false);

        return new IssuanceContext(
            client.Id,
            client.ClientId,
            certificate!,
            activeAssignedScopes,
            activeAssignedRoles);
    }

    private static string ComputeCertificateThumbprintHex(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
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

    private async Task<MachineClient> AuthenticateClientAsync(
        string? clientId,
        X509Certificate2? certificate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new OAuthException("invalid_request", "client_id is required.", _badRequestStatusCode);
        }

        if (certificate is null)
        {
            throw new OAuthException("invalid_client", "Client certificate is missing or invalid.", _unauthorizedStatusCode);
        }

        var client = await this._clientRepository.GetByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (client is null || !client.Active)
        {
            throw new OAuthException("invalid_client", "Client authentication failed.", _unauthorizedStatusCode);
        }

        await this.ValidateClientCertificateAsync(client, certificate, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private async Task ValidateClientCertificateAsync(
        MachineClient client,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var actualThumbprint = ComputeCertificateThumbprintHex(certificate);
        var registeredCertificate = await this._certificateRepository
            .GetByThumbprintAsync(client.Id, actualThumbprint, cancellationToken)
            .ConfigureAwait(false);

        if (registeredCertificate is not null)
        {
            ValidateRegisteredCertificate(registeredCertificate);
            return;
        }

        ValidateClientCertificate(client, certificate);
    }

    private async Task<IReadOnlyList<string>> ResolveActiveAssignedScopesAsync(
        MachineClient client,
        CancellationToken cancellationToken)
    {
        var activeScopes = new List<string>();
        foreach (var scopeValue in client.GetAssignedScopes())
        {
            if (await this._scopeRepository.ExistsActiveByValueAsync(scopeValue, cancellationToken).ConfigureAwait(false))
            {
                activeScopes.Add(scopeValue);
            }
        }

        return activeScopes;
    }

    private async Task<IReadOnlyList<string>> ResolveActiveAssignedRolesAsync(
        MachineClient client,
        CancellationToken cancellationToken)
    {
        var activeRoles = new List<string>();
        foreach (var roleValue in client.GetAssignedRoles())
        {
            if (await this._roleRepository.ExistsActiveByValueAsync(roleValue, cancellationToken).ConfigureAwait(false))
            {
                activeRoles.Add(roleValue);
            }
        }

        return activeRoles;
    }
}
