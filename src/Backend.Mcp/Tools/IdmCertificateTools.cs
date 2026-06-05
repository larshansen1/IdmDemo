using System.ComponentModel;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Scim;
using Backend.Mcp.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmCertificateTools
{
    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;

    public IdmCertificateTools(IIdmApiClient apiClient, IMcpMutationGuard mutationGuard)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
    }

    [McpServerTool(Name = "idm_register_external_client_certificate", ReadOnly = false, Destructive = false)]
    [Description("Register an externally issued public certificate for a machine client.")]
    public async Task<IdmApiCallResult<CertificateResponse>> RegisterExternalClientCertificateAsync(
        string clientId,
        string certificatePem,
        string? displayName = null,
        DateTimeOffset? expiresAt = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();

        var request = new CreateCertificateRequest
        {
            Mode = "external",
            CertificatePem = certificatePem,
            DisplayName = displayName,
            ExpiresAt = expiresAt,
        };

        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        return await this._apiClient.CreateCertificateAsync(instance, clientRecordId, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_issue_client_certificate_from_csr", ReadOnly = false, Destructive = false)]
    [Description("Issue a client certificate from a caller-provided CSR. validityDays is optional and must be between 1 and 90.")]
    public async Task<CallToolResult> IssueClientCertificateFromCsrAsync(
        string clientId,
        string certificateSigningRequestPem,
        string? displayName = null,
        int? validityDays = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();

        var request = new CreateCertificateRequest
        {
            Mode = "csr",
            CertificateSigningRequestPem = certificateSigningRequestPem,
            DisplayName = displayName,
            ValidityDays = validityDays,
        };

        try
        {
            var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
            var result = await this._apiClient.CreateCertificateAsync(instance, clientRecordId, request, cancellationToken).ConfigureAwait(false);
            return IdmToolHelpers.CreateIssuedCertificateToolResult(result);
        }
        catch (IdmApiException exception)
        {
            return IdmToolHelpers.CreateApiErrorToolResult(exception);
        }
        catch (McpConfigurationException exception)
        {
            return IdmToolHelpers.CreateToolResult(new { error = exception.Message }, true);
        }
    }

    [McpServerTool(Name = "idm_list_client_certificates", ReadOnly = true)]
    [Description("List certificates registered for a machine client.")]
    public async Task<IdmApiCallResult<ScimListResponse<CertificateResponse>>> ListClientCertificatesAsync(
        string clientId,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        return await this._apiClient.ListCertificatesAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_client_certificate", ReadOnly = true)]
    [Description("Get one certificate registered for a machine client.")]
    public async Task<IdmApiCallResult<CertificateResponse>> GetClientCertificateAsync(
        string clientId,
        Guid certificateId,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        return await this._apiClient.GetCertificateAsync(instance, clientRecordId, certificateId, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_revoke_client_certificate", ReadOnly = false, Destructive = true)]
    [Description("Revoke a machine-client certificate. Requires confirm: true.")]
    public async Task<IdmApiCallResult<CertificateResponse>> RevokeClientCertificateAsync(
        string clientId,
        Guid certificateId,
        bool confirm,
        string? reason = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureDestructiveAllowed(confirm);

        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        return await this._apiClient
            .RevokeCertificateAsync(instance, clientRecordId, certificateId, new RevokeCertificateRequest { Reason = reason }, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_certificate_authority", ReadOnly = true)]
    [Description("Get the local development CA public certificate.")]
    public async Task<IdmApiCallResult<CertificateAuthorityResponse>> GetCertificateAuthorityAsync(
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetCertificateAuthorityAsync(instance, cancellationToken).ConfigureAwait(false);
    }
}
