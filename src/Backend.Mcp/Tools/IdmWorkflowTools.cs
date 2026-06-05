using System.ComponentModel;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Mcp.Api;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmWorkflowTools
{
    private static readonly Action<ILogger, string, Exception?> _onboardingFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6001, nameof(_onboardingFailed)),
            "Machine-client onboarding failed with correlation id {CorrelationId}.");

    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;
    private readonly ILogger<IdmWorkflowTools> _logger;

    public IdmWorkflowTools(
        IIdmApiClient apiClient,
        IMcpMutationGuard mutationGuard,
        ILogger<IdmWorkflowTools> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);
        ArgumentNullException.ThrowIfNull(logger);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
        this._logger = logger;
    }

    [McpServerTool(Name = "idm_get_authorization_server_metadata", ReadOnly = true)]
    [Description("Inspect authorization server discovery metadata.")]
    public async Task<IdmApiCallResult<DiscoveryResponse>> GetAuthorizationServerMetadataAsync(
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetDiscoveryAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_jwks", ReadOnly = true)]
    [Description("Inspect public JWT signing keys exposed by JWKS.")]
    public async Task<IdmApiCallResult<JwksResponse>> GetJwksAsync(
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetJwksAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_inspect_client_credential_status", ReadOnly = true)]
    [Description("Inspect machine-client credential status using client and certificate metadata.")]
    public async Task<ClientCredentialStatusResult> InspectClientCredentialStatusAsync(
        string clientId,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        var client = await this._apiClient.GetClientAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
        var certificates = await this._apiClient.ListCertificatesAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
        var activeCertificates = certificates.Value.Resources
            .Where(certificate => string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ClientCredentialStatusResult(
            client.Instance,
            client.CorrelationId,
            client.Value.Id,
            client.Value.ClientId,
            client.Value.Active,
            certificates.Value.TotalResults,
            activeCertificates.Length,
            activeCertificates.Select(certificate => certificate.ExpiresAt).Order().FirstOrDefault());
    }

    [McpServerTool(Name = "idm_rotate_machine_client_certificate", ReadOnly = false, Destructive = false)]
    [Description("Issue a replacement machine-client certificate from a CSR and optionally revoke a previous certificate.")]
    public async Task<RotateMachineClientCertificateResult> RotateMachineClientCertificateAsync(
        string clientId,
        string certificateSigningRequestPem,
        string? displayName = null,
        int? validityDays = null,
        Guid? revokeCertificateId = null,
        bool confirmRevoke = false,
        string? reason = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        IdmToolHelpers.RequireText(clientId, nameof(clientId));
        IdmToolHelpers.RequireText(certificateSigningRequestPem, nameof(certificateSigningRequestPem));

        if (revokeCertificateId is not null)
        {
            this._mutationGuard.EnsureToolAllowed(
                "idm_revoke_client_certificate",
                CreateConfirmArguments(confirmRevoke));
        }

        var steps = new List<WorkflowStepResult>();
        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        steps.Add(new WorkflowStepResult("resolve_client", IdmToolHelpers.Succeeded, null, $"Resolved client record {clientRecordId:D}."));

        var certificates = await this._apiClient.ListCertificatesAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
        var activeCertificates = certificates.Value.Resources
            .Where(certificate => string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(certificate => certificate.ExpiresAt)
            .ToArray();
        steps.Add(new WorkflowStepResult("inspect_existing_certificates", IdmToolHelpers.Succeeded, certificates.CorrelationId, null));

        var issued = await this._apiClient.CreateCertificateAsync(
            instance,
            clientRecordId,
            new CreateCertificateRequest
            {
                Mode = "csr",
                CertificateSigningRequestPem = certificateSigningRequestPem,
                DisplayName = displayName,
                ValidityDays = validityDays,
            },
            cancellationToken).ConfigureAwait(false);
        steps.Add(new WorkflowStepResult("issue_certificate", IdmToolHelpers.Succeeded, issued.CorrelationId, null));

        CertificateResponse? revokedCertificate = null;
        if (revokeCertificateId is not null)
        {
            var revoked = await this._apiClient
                .RevokeCertificateAsync(
                    instance,
                    clientRecordId,
                    revokeCertificateId.Value,
                    new RevokeCertificateRequest { Reason = reason },
                    cancellationToken)
                .ConfigureAwait(false);
            revokedCertificate = revoked.Value;
            steps.Add(new WorkflowStepResult("revoke_previous_certificate", IdmToolHelpers.Succeeded, revoked.CorrelationId, null));
        }
        else
        {
            steps.Add(new WorkflowStepResult("revoke_previous_certificate", "skipped", null, "No previous certificate id was supplied."));
        }

        return new RotateMachineClientCertificateResult(
            issued.Instance,
            IdmToolHelpers.Succeeded,
            clientId,
            clientRecordId,
            activeCertificates.Length,
            issued.Value,
            revokedCertificate,
            steps,
            [
                "Deploy the returned certificate with the private key that generated the CSR.",
                "After the new credential is confirmed in use, revoke any superseded certificate.",
            ]);
    }

    [McpServerTool(Name = "idm_prepare_dpop_client_credential_instructions", ReadOnly = true)]
    [Description("Prepare DPoP-bound client credential setup instructions for a machine client.")]
    public async Task<DpopClientCredentialInstructionsResult> PrepareDpopClientCredentialInstructionsAsync(
        string clientId,
        string? mcpAudience = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        IdmToolHelpers.RequireText(clientId, nameof(clientId));

        var discovery = await this._apiClient.GetDiscoveryAsync(instance, cancellationToken).ConfigureAwait(false);
        var audience = string.IsNullOrWhiteSpace(mcpAudience) ? "idm-demo-mcp" : mcpAudience.Trim();

        return new DpopClientCredentialInstructionsResult(
            discovery.Instance,
            discovery.CorrelationId,
            clientId,
            audience,
            discovery.Value,
            [
                new WorkflowStepResult("inspect_authorization_server", IdmToolHelpers.Succeeded, discovery.CorrelationId, null),
                new WorkflowStepResult("prepare_client_material", "manual", null, "Generate the DPoP key and certificate private key outside IdmDemo."),
                new WorkflowStepResult("request_token", "manual", null, "Send a fresh DPoP proof when requesting the access token."),
                new WorkflowStepResult("call_hosted_mcp", "manual", null, "Send the DPoP-bound access token and a fresh DPoP proof on each hosted MCP request."),
            ],
            [
                "Generate an asymmetric DPoP key pair in the caller-controlled environment.",
                "Generate a separate private key and CSR for the machine-client certificate.",
                "Use idm_issue_client_certificate_from_csr or idm_rotate_machine_client_certificate to obtain the public certificate.",
                $"Request a client_credentials access token from {discovery.Value.TokenEndpoint} with audience '{audience}' and a DPoP proof signed by the DPoP key.",
                "Call hosted MCP with Authorization: DPoP <access token> and a new DPoP proof bound to the MCP request method and URI.",
            ]);
    }

    [McpServerTool(Name = "idm_preflight_machine_client_deployment", ReadOnly = true)]
    [Description("Preflight a machine client before deployment by checking activation, scopes, roles, and active certificates.")]
    public async Task<MachineClientDeploymentPreflightResult> PreflightMachineClientDeploymentAsync(
        string clientId,
        string[]? requiredRoles = null,
        string[]? requiredScopes = null,
        int minimumCertificateValidityDays = 7,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        IdmToolHelpers.RequireText(clientId, nameof(clientId));

        var clientRecordId = await IdmToolHelpers.ResolveClientRecordIdAsync(this._apiClient, instance, clientId, cancellationToken).ConfigureAwait(false);
        var client = await this._apiClient.GetClientAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
        var certificates = await this._apiClient.ListCertificatesAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
        var activeCertificates = certificates.Value.Resources
            .Where(certificate => string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(certificate => certificate.ExpiresAt)
            .ToArray();

        var blockingIssues = new List<string>();
        var warnings = new List<string>();
        var suggestedNextActions = new List<string>();

        if (!client.Value.Active)
        {
            blockingIssues.Add("Machine client is inactive.");
            suggestedNextActions.Add("Activate the machine client before deployment.");
        }

        AddMissingAssignments("role", requiredRoles, client.Value.AssignedRoles, blockingIssues, suggestedNextActions);
        AddMissingAssignments("scope", requiredScopes, client.Value.AssignedScopes, blockingIssues, suggestedNextActions);

        if (activeCertificates.Length == 0)
        {
            blockingIssues.Add("Machine client has no active certificate.");
            suggestedNextActions.Add("Issue or register an active client certificate before deployment.");
        }
        else
        {
            var minimumExpiry = DateTimeOffset.UtcNow.AddDays(Math.Max(0, minimumCertificateValidityDays));
            var expiringSoon = activeCertificates
                .Where(certificate => certificate.ExpiresAt <= minimumExpiry)
                .ToArray();
            if (expiringSoon.Length > 0)
            {
                warnings.Add($"One or more active certificates expire within {minimumCertificateValidityDays} days.");
                suggestedNextActions.Add("Rotate the machine-client certificate before deployment if the deployment window depends on this credential.");
            }
        }

        if (blockingIssues.Count == 0 && warnings.Count == 0)
        {
            suggestedNextActions.Add("Proceed with deployment using an active certificate and DPoP-bound token request.");
        }

        return new MachineClientDeploymentPreflightResult(
            client.Instance,
            client.CorrelationId,
            client.Value.ClientId,
            clientRecordId,
            blockingIssues.Count == 0,
            client.Value,
            certificates.Value.TotalResults,
            activeCertificates.Length,
            activeCertificates.Select(certificate => certificate.ExpiresAt).Order().FirstOrDefault(),
            activeCertificates,
            blockingIssues,
            warnings,
            suggestedNextActions);
    }

    [McpServerTool(Name = "idm_onboard_machine_client", ReadOnly = false, Destructive = false)]
    [Description("Create or update a machine client, assign roles/scopes, and optionally register or issue an initial certificate.")]
    public async Task<OnboardMachineClientResult> OnboardMachineClientAsync(
        string clientId,
        string? displayName = null,
        string[]? assignedRoles = null,
        string[]? assignedScopes = null,
        Guid? clientRecordId = null,
        string? certificateMode = null,
        string? certificateSigningRequestPem = null,
        string? certificatePem = null,
        string? certificateDisplayName = null,
        int? certificateValidityDays = null,
        DateTimeOffset? certificateExpiresAt = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        IdmToolHelpers.RequireText(clientId, nameof(clientId));

        var steps = new List<OnboardMachineClientStep>();
        ClientResponse? client = null;
        CertificateResponse? certificate = null;

        try
        {
            client = await this.CreateOrUpdateOnboardedClientAsync(
                instance,
                clientId,
                displayName,
                assignedRoles ?? [],
                assignedScopes ?? [],
                clientRecordId,
                steps,
                cancellationToken).ConfigureAwait(false);

            certificate = await this.CreateOnboardingCertificateAsync(
                instance,
                client,
                certificateMode,
                certificateSigningRequestPem,
                certificatePem,
                certificateDisplayName,
                certificateValidityDays,
                certificateExpiresAt,
                steps,
                cancellationToken).ConfigureAwait(false);
        }
        catch (IdmApiException exception)
        {
            _onboardingFailed(this._logger, exception.CorrelationId, exception);

            steps.Add(new OnboardMachineClientStep("onboarding", IdmToolHelpers.Failed, exception.CorrelationId, exception.Message));
            return CreateOnboardResult(instance, "partial_failure", client, certificate, assignedRoles, assignedScopes, steps);
        }
        catch (McpToolException exception)
        {
            steps.Add(new OnboardMachineClientStep("onboarding", IdmToolHelpers.Failed, null, exception.Message));
            return CreateOnboardResult(instance, "partial_failure", client, certificate, assignedRoles, assignedScopes, steps);
        }

        return CreateOnboardResult(instance, IdmToolHelpers.Succeeded, client, certificate, assignedRoles, assignedScopes, steps);
    }

    private static OnboardMachineClientResult CreateOnboardResult(
        string? instance,
        string status,
        ClientResponse? client,
        CertificateResponse? certificate,
        IReadOnlyList<string>? assignedRoles,
        IReadOnlyList<string>? assignedScopes,
        IReadOnlyList<OnboardMachineClientStep> steps)
    {
        var nextSteps = certificate is null
            ? new[] { "Register or issue a client certificate before requesting tokens." }
            : new[] { "Store the private key outside the server and use the certificate for mTLS client authentication." };

        return new OnboardMachineClientResult(
            instance ?? string.Empty,
            status,
            client,
            certificate,
            assignedRoles ?? [],
            assignedScopes ?? [],
            steps,
            nextSteps);
    }

    private static void AddMissingAssignments(
        string assignmentName,
        string[]? required,
        IReadOnlyList<string> assigned,
        List<string> blockingIssues,
        List<string> suggestedNextActions)
    {
        if (required is not { Length: > 0 })
        {
            return;
        }

        var assignedSet = assigned.ToHashSet(StringComparer.Ordinal);
        var missing = required
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !assignedSet.Contains(value))
            .ToArray();

        foreach (var value in missing)
        {
            blockingIssues.Add($"Machine client is missing required {assignmentName} '{value}'.");
        }

        if (missing.Length > 0)
        {
            suggestedNextActions.Add($"Assign the missing {assignmentName}s before deployment.");
        }
    }

    private static Dictionary<string, JsonElement> CreateConfirmArguments(bool confirm)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["confirm"] = JsonSerializer.SerializeToElement(confirm),
        };
    }

    private async Task<ClientResponse> CreateOrUpdateOnboardedClientAsync(
        string? instance,
        string clientId,
        string? displayName,
        IReadOnlyList<string> assignedRoles,
        IReadOnlyList<string> assignedScopes,
        Guid? clientRecordId,
        List<OnboardMachineClientStep> steps,
        CancellationToken cancellationToken)
    {
        if (clientRecordId is not null)
        {
            var update = new UpdateClientRequest
            {
                ClientId = clientId,
                DisplayName = displayName,
                Active = true,
                AssignedRoles = assignedRoles,
                AssignedScopes = assignedScopes,
            };
            var updated = await this._apiClient.UpdateClientAsync(instance, clientRecordId.Value, update, cancellationToken).ConfigureAwait(false);
            steps.Add(new OnboardMachineClientStep("update_client", IdmToolHelpers.Succeeded, updated.CorrelationId, null));
            return updated.Value;
        }

        var found = await this._apiClient.ListClientsAsync(instance, $"clientId eq \"{clientId}\"", cancellationToken).ConfigureAwait(false);
        var existing = found.Value.Resources.Count > 0 ? found.Value.Resources[0] : null;
        if (existing is not null)
        {
            var update = new UpdateClientRequest
            {
                ClientId = clientId,
                DisplayName = displayName ?? existing.DisplayName,
                Active = existing.Active,
                AssignedRoles = assignedRoles,
                AssignedScopes = assignedScopes,
            };
            var updated = await this._apiClient.UpdateClientAsync(instance, Guid.Parse(existing.Id), update, cancellationToken).ConfigureAwait(false);
            steps.Add(new OnboardMachineClientStep("update_client", IdmToolHelpers.Succeeded, updated.CorrelationId, null));
            return updated.Value;
        }

        var create = new CreateClientRequest
        {
            ClientId = clientId,
            DisplayName = displayName,
            Active = true,
            AssignedRoles = assignedRoles,
            AssignedScopes = assignedScopes,
        };
        var created = await this._apiClient.CreateClientAsync(instance, create, cancellationToken).ConfigureAwait(false);
        steps.Add(new OnboardMachineClientStep("create_client", IdmToolHelpers.Succeeded, created.CorrelationId, null));
        return created.Value;
    }

    private async Task<CertificateResponse?> CreateOnboardingCertificateAsync(
        string? instance,
        ClientResponse client,
        string? certificateMode,
        string? certificateSigningRequestPem,
        string? certificatePem,
        string? certificateDisplayName,
        int? certificateValidityDays,
        DateTimeOffset? certificateExpiresAt,
        List<OnboardMachineClientStep> steps,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(certificateMode))
        {
            steps.Add(new OnboardMachineClientStep("certificate", "skipped", null, "No certificate mode was requested."));
            return null;
        }

        CreateCertificateRequest request = certificateMode.Trim().ToUpperInvariant() switch
        {
            "CSR" => new CreateCertificateRequest
            {
                Mode = "csr",
                CertificateSigningRequestPem = certificateSigningRequestPem,
                DisplayName = certificateDisplayName,
                ValidityDays = certificateValidityDays,
            },
            "EXTERNAL" => new CreateCertificateRequest
            {
                Mode = "external",
                CertificatePem = certificatePem,
                DisplayName = certificateDisplayName,
                ExpiresAt = certificateExpiresAt,
            },
            _ => throw new McpToolException("certificateMode must be either 'csr' or 'external'."),
        };

        var created = await this._apiClient
            .CreateCertificateAsync(instance, Guid.Parse(client.Id), request, cancellationToken)
            .ConfigureAwait(false);

        steps.Add(new OnboardMachineClientStep("certificate", IdmToolHelpers.Succeeded, created.CorrelationId, null));
        return created.Value;
    }
}
