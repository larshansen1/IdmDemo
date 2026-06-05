using System.ComponentModel;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Application.Scim;
using Backend.Domain.Exceptions;
using Backend.Mcp.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmMachineClientTools
{
    private const int _maxMachineClientFilterLength = 256;

    private static readonly HashSet<string> _allowedMachineClientFilterAttributes = new(StringComparer.Ordinal)
    {
        "clientId",
    };

    private static readonly Action<ILogger, string, string?, Exception?> _machineClientFilterAccepted =
        LoggerMessage.Define<string, string?>(
            LogLevel.Information,
            new EventId(6002, nameof(_machineClientFilterAccepted)),
            "Machine-client list requested with SCIM filter {Filter} on instance {Instance}.");

    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;
    private readonly ILogger<IdmMachineClientTools> _logger;

    public IdmMachineClientTools(
        IIdmApiClient apiClient,
        IMcpMutationGuard mutationGuard,
        ILogger<IdmMachineClientTools> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);
        ArgumentNullException.ThrowIfNull(logger);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
        this._logger = logger;
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
    [Description("List machine-client identities. Optional SCIM filter; only clientId eq \"value\" is allowed.")]
    public async Task<CallToolResult> ListMachineClientsAsync(
        string? filter = null,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validatedFilter = ValidateMachineClientFilter(filter);
            if (validatedFilter is not null)
            {
                _machineClientFilterAccepted(this._logger, validatedFilter, instance, null);
            }

            var result = await this._apiClient.ListClientsAsync(instance, validatedFilter, cancellationToken).ConfigureAwait(false);
            return await this.CreateMachineClientListToolResultAsync(result, instance, cancellationToken).ConfigureAwait(false);
        }
        catch (ValidationException exception)
        {
            return IdmToolHelpers.CreateToolResult(new { error = exception.Message }, true);
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
        return new OperationResult(result.Instance, result.CorrelationId, IdmToolHelpers.Succeeded);
    }

    private static string? ValidateMachineClientFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var trimmed = filter.Trim();
        if (trimmed.Length > _maxMachineClientFilterLength)
        {
            throw new ValidationException(
                $"Machine-client filter must be {_maxMachineClientFilterLength} characters or fewer.");
        }

        var parsed = ScimFilterParser.Parse(trimmed);
        if (!_allowedMachineClientFilterAttributes.Contains(parsed.AttributeName))
        {
            throw new ValidationException(
                $"Filter on '{parsed.AttributeName}' is not supported. Use 'clientId'.");
        }

        return trimmed;
    }

    private async Task<CallToolResult> CreateMachineClientListToolResultAsync(
        IdmApiCallResult<ScimListResponse<ClientResponse>> result,
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

        return IdmToolHelpers.CreateToolResult(
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
}
