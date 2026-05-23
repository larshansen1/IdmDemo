using System.ComponentModel;
using System.Text.Json;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scopes;
using Backend.Application.Models.Users;
using Backend.Mcp.Api;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmAdminTools
{
    private const string _succeeded = "succeeded";
    private const string _failed = "failed";
    private static readonly string[] _issuedCertificateNextSteps =
    [
        "Return certificatePem to the caller.",
        "Use certificatePem with the private key that generated the CSR.",
    ];

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, string, Exception?> _onboardingFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6001, nameof(_onboardingFailed)),
            "Machine-client onboarding failed with correlation id {CorrelationId}.");

    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;
    private readonly ILogger<IdmAdminTools> _logger;

    public IdmAdminTools(
        IIdmApiClient apiClient,
        IMcpMutationGuard mutationGuard,
        ILogger<IdmAdminTools> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);
        ArgumentNullException.ThrowIfNull(logger);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
        this._logger = logger;
    }

    [McpServerTool(Name = "idm_create_user", ReadOnly = false, Destructive = false)]
    [Description("Create a user identity record through the IdM SCIM API.")]
    public async Task<IdmApiCallResult<UserResponse>> CreateUserAsync(
        CreateUserRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.CreateUserAsync(instance, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_user", ReadOnly = true)]
    [Description("Get a user identity record by IdM user id.")]
    public async Task<IdmApiCallResult<UserResponse>> GetUserAsync(
        Guid id,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetUserAsync(instance, id, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_update_user", ReadOnly = false, Destructive = false)]
    [Description("Update a user identity record through the IdM SCIM API.")]
    public async Task<IdmApiCallResult<UserResponse>> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.UpdateUserAsync(instance, id, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_delete_user", ReadOnly = false, Destructive = true)]
    [Description("Delete a user identity record. Requires confirm: true.")]
    public async Task<OperationResult> DeleteUserAsync(
        Guid id,
        bool confirm,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureDestructiveAllowed(confirm);
        var result = await this._apiClient.DeleteUserAsync(instance, id, cancellationToken).ConfigureAwait(false);
        return new OperationResult(result.Instance, result.CorrelationId, _succeeded);
    }

    [McpServerTool(Name = "idm_create_machine_client", ReadOnly = false, Destructive = false)]
    [Description("Create a machine-client identity through the IdM SCIM-shaped API.")]
    public async Task<IdmApiCallResult<ClientResponse>> CreateMachineClientAsync(
        CreateClientRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.CreateClientAsync(instance, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_machine_client", ReadOnly = true)]
    [Description("Get a machine-client identity by IdM client record id.")]
    public async Task<IdmApiCallResult<ClientResponse>> GetMachineClientAsync(
        Guid id,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetClientAsync(instance, id, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_list_machine_clients", ReadOnly = true)]
    [Description("List machine-client identities. Optional SCIM filter, for example: clientId eq \"orders-service\".")]
    public async Task<CallToolResult> ListMachineClientsAsync(
        string? filter = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await this._apiClient.ListClientsAsync(instance, filter, cancellationToken).ConfigureAwait(false);
            return await this.CreateMachineClientListToolResultAsync(result, instance, cancellationToken).ConfigureAwait(false);
        }
        catch (IdmApiException exception)
        {
            return CreateApiErrorToolResult(exception);
        }
        catch (McpConfigurationException exception)
        {
            return CreateToolResult(new { error = exception.Message }, true);
        }
    }

    [McpServerTool(Name = "idm_update_machine_client", ReadOnly = false, Destructive = false)]
    [Description("Update a machine-client identity through the IdM SCIM-shaped API.")]
    public async Task<IdmApiCallResult<ClientResponse>> UpdateMachineClientAsync(
        Guid id,
        UpdateClientRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.UpdateClientAsync(instance, id, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_delete_machine_client", ReadOnly = false, Destructive = true)]
    [Description("Delete a machine-client identity. Requires confirm: true.")]
    public async Task<OperationResult> DeleteMachineClientAsync(
        Guid id,
        bool confirm,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureDestructiveAllowed(confirm);
        var result = await this._apiClient.DeleteClientAsync(instance, id, cancellationToken).ConfigureAwait(false);
        return new OperationResult(result.Instance, result.CorrelationId, _succeeded);
    }

    [McpServerTool(Name = "idm_create_global_role", ReadOnly = false, Destructive = false)]
    [Description("Create a global role catalog entry.")]
    public async Task<IdmApiCallResult<RoleResponse>> CreateGlobalRoleAsync(
        CreateRoleRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.CreateRoleAsync(instance, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_update_global_role", ReadOnly = false, Destructive = false)]
    [Description("Update a global role catalog entry.")]
    public async Task<IdmApiCallResult<RoleResponse>> UpdateGlobalRoleAsync(
        Guid id,
        UpdateRoleRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.UpdateRoleAsync(instance, id, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_delete_global_role", ReadOnly = false, Destructive = true)]
    [Description("Delete a global role catalog entry. Requires confirm: true.")]
    public async Task<OperationResult> DeleteGlobalRoleAsync(
        Guid id,
        bool confirm,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureDestructiveAllowed(confirm);
        var result = await this._apiClient.DeleteRoleAsync(instance, id, cancellationToken).ConfigureAwait(false);
        return new OperationResult(result.Instance, result.CorrelationId, _succeeded);
    }

    [McpServerTool(Name = "idm_create_global_scope", ReadOnly = false, Destructive = false)]
    [Description("Create a global scope catalog entry.")]
    public async Task<IdmApiCallResult<ScopeResponse>> CreateGlobalScopeAsync(
        CreateScopeRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.CreateScopeAsync(instance, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_update_global_scope", ReadOnly = false, Destructive = false)]
    [Description("Update a global scope catalog entry.")]
    public async Task<IdmApiCallResult<ScopeResponse>> UpdateGlobalScopeAsync(
        Guid id,
        UpdateScopeRequest request,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureMutationAllowed();
        return await this._apiClient.UpdateScopeAsync(instance, id, request, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_delete_global_scope", ReadOnly = false, Destructive = true)]
    [Description("Delete a global scope catalog entry. Requires confirm: true.")]
    public async Task<OperationResult> DeleteGlobalScopeAsync(
        Guid id,
        bool confirm,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        this._mutationGuard.EnsureDestructiveAllowed(confirm);
        var result = await this._apiClient.DeleteScopeAsync(instance, id, cancellationToken).ConfigureAwait(false);
        return new OperationResult(result.Instance, result.CorrelationId, _succeeded);
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

        var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
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
            var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
            var result = await this._apiClient.CreateCertificateAsync(instance, clientRecordId, request, cancellationToken).ConfigureAwait(false);
            return CreateIssuedCertificateToolResult(result);
        }
        catch (IdmApiException exception)
        {
            return CreateApiErrorToolResult(exception);
        }
        catch (McpConfigurationException exception)
        {
            return CreateToolResult(new { error = exception.Message }, true);
        }
    }

    [McpServerTool(Name = "idm_list_client_certificates", ReadOnly = true)]
    [Description("List certificates registered for a machine client.")]
    public async Task<IdmApiCallResult<Backend.Application.Models.Scim.ScimListResponse<CertificateResponse>>> ListClientCertificatesAsync(
        string clientId,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
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
        var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
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

        var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
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

    [McpServerTool(Name = "idm_get_authorization_server_metadata", ReadOnly = true)]
    [Description("Inspect authorization server discovery metadata.")]
    public async Task<IdmApiCallResult<Backend.Application.Models.Auth.DiscoveryResponse>> GetAuthorizationServerMetadataAsync(
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        return await this._apiClient.GetDiscoveryAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "idm_get_jwks", ReadOnly = true)]
    [Description("Inspect public JWT signing keys exposed by JWKS.")]
    public async Task<IdmApiCallResult<Backend.Application.Models.Auth.JwksResponse>> GetJwksAsync(
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
        var clientRecordId = await this.ResolveClientRecordIdAsync(instance, clientId, cancellationToken).ConfigureAwait(false);
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
        RequireText(clientId, nameof(clientId));

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

            steps.Add(new OnboardMachineClientStep("onboarding", _failed, exception.CorrelationId, exception.Message));
            return CreateOnboardResult(instance, "partial_failure", client, certificate, assignedRoles, assignedScopes, steps);
        }
        catch (McpToolException exception)
        {
            steps.Add(new OnboardMachineClientStep("onboarding", _failed, null, exception.Message));
            return CreateOnboardResult(instance, "partial_failure", client, certificate, assignedRoles, assignedScopes, steps);
        }

        return CreateOnboardResult(instance, _succeeded, client, certificate, assignedRoles, assignedScopes, steps);
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

    private static CallToolResult CreateToolResult<T>(T value, bool isError)
    {
        return new CallToolResult
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(value, _jsonOptions),
                },
            ],
        };
    }

    private static CallToolResult CreateApiErrorToolResult(IdmApiException exception)
    {
        return CreateToolResult(
            new
            {
                error = exception.Message,
                statusCode = (int)exception.StatusCode,
                exception.CorrelationId,
            },
            true);
    }

    private static CallToolResult CreateIssuedCertificateToolResult(IdmApiCallResult<CertificateResponse> result)
    {
        var metadata = new
        {
            result.Instance,
            result.CorrelationId,
            certificatePem = result.Value.CertificatePem,
            certificate = result.Value,
            nextSteps = _issuedCertificateNextSteps,
        };

        return new CallToolResult
        {
            IsError = false,
            Content =
            [
                new TextContentBlock
                {
                    Text = result.Value.CertificatePem,
                },
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(metadata, _jsonOptions),
                },
            ],
        };
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpToolException($"{parameterName} is required.");
        }
    }

    private static string EscapeScimFilterValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private async Task<CallToolResult> CreateMachineClientListToolResultAsync(
        IdmApiCallResult<Backend.Application.Models.Scim.ScimListResponse<ClientResponse>> result,
        string? instance,
        CancellationToken cancellationToken)
    {
        var clients = new List<object>();

        foreach (var client in result.Value.Resources)
        {
            if (!Guid.TryParse(client.Id, out var clientRecordId))
            {
                clients.Add(new
                {
                    client,
                    certificateSummary = new
                    {
                        certificateCount = 0,
                        activeCertificateCount = 0,
                        nextCertificateExpiry = (DateTimeOffset?)null,
                        warning = $"Machine client '{client.ClientId}' has an invalid record id '{client.Id}'.",
                    },
                    activeCertificates = Array.Empty<object>(),
                });
                continue;
            }

            var certificates = await this._apiClient.ListCertificatesAsync(instance, clientRecordId, cancellationToken).ConfigureAwait(false);
            var activeCertificates = certificates.Value.Resources
                .Where(certificate => string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase))
                .OrderBy(certificate => certificate.ExpiresAt)
                .Select(certificate => new
                {
                    certificate.Id,
                    certificate.DisplayName,
                    certificate.Subject,
                    certificate.Issuer,
                    certificate.ThumbprintSha256,
                    certificate.NotBefore,
                    certificate.ExpiresAt,
                    certificate.Status,
                })
                .ToArray();

            clients.Add(new
            {
                id = client.Id,
                clientId = client.ClientId,
                client.DisplayName,
                client.Active,
                client.AssignedRoles,
                client.AssignedScopes,
                legacySingleCertificate = new
                {
                    client.CertificateThumbprintSha256,
                    client.CertificateSubject,
                    client.CertificateExpiresAt,
                    note = "Legacy single-certificate fields do not reflect the certificate collection.",
                },
                certificateSummary = new
                {
                    certificateCount = certificates.Value.TotalResults,
                    activeCertificateCount = activeCertificates.Length,
                    nextCertificateExpiry = activeCertificates.Length > 0 ? activeCertificates[0].ExpiresAt : (DateTimeOffset?)null,
                },
                activeCertificates,
            });
        }

        return CreateToolResult(
            new
            {
                result.Instance,
                result.CorrelationId,
                result.Value.TotalResults,
                result.Value.StartIndex,
                itemsPerPage = clients.Count,
                clients,
            },
            false);
    }

    private async Task<Guid> ResolveClientRecordIdAsync(
        string? instance,
        string clientId,
        CancellationToken cancellationToken)
    {
        RequireText(clientId, nameof(clientId));

        if (Guid.TryParse(clientId, out var clientRecordId))
        {
            return clientRecordId;
        }

        var filter = $"clientId eq \"{EscapeScimFilterValue(clientId)}\"";
        var found = await this._apiClient.ListClientsAsync(instance, filter, cancellationToken).ConfigureAwait(false);
        var matches = found.Value.Resources
            .Where(client => string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            0 => throw new McpToolException($"Machine client '{clientId}' was not found."),
            1 when Guid.TryParse(matches[0].Id, out var id) => id,
            1 => throw new McpToolException($"Machine client '{clientId}' has an invalid record id '{matches[0].Id}'."),
            _ => throw new McpToolException($"Machine client '{clientId}' matched multiple records."),
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
            steps.Add(new OnboardMachineClientStep("update_client", _succeeded, updated.CorrelationId, null));
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
            steps.Add(new OnboardMachineClientStep("update_client", _succeeded, updated.CorrelationId, null));
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
        steps.Add(new OnboardMachineClientStep("create_client", _succeeded, created.CorrelationId, null));
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

        steps.Add(new OnboardMachineClientStep("certificate", _succeeded, created.CorrelationId, null));
        return created.Value;
    }
}
