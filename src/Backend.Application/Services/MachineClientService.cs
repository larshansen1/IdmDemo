using System.Text.Json;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Application.Scim;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class MachineClientService : IMachineClientService
{
    private readonly IMachineClientRepository _clientRepository;
    private readonly IGlobalRoleRepository _roleRepository;
    private readonly IGlobalScopeRepository _scopeRepository;
    private readonly ILogger<MachineClientService> _logger;

    public MachineClientService(
        IMachineClientRepository clientRepository,
        IGlobalRoleRepository roleRepository,
        IGlobalScopeRepository scopeRepository,
        ILogger<MachineClientService> logger)
    {
        this._clientRepository = clientRepository;
        this._roleRepository = roleRepository;
        this._scopeRepository = scopeRepository;
        this._logger = logger;
    }

    public async Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateClientId(request.ClientId);
        var certificateThumbprint = NormalizeThumbprint(request.CertificateThumbprintSha256);
        var assignedScopes = AccessManagementValidation.ValidateNames(request.AssignedScopes, "assignedScopes");
        var assignedRoles = AccessManagementValidation.ValidateNames(request.AssignedRoles, "assignedRoles");
        await this.ValidateAssignmentsAsync(assignedScopes, assignedRoles, cancellationToken).ConfigureAwait(false);

        if (await this._clientRepository.ExistsByClientIdAsync(request.ClientId!, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("clientId", request.ClientId!);
        }

        var client = MachineClient.Create(request.ClientId!, request.DisplayName);
        client.UpdateCertificate(certificateThumbprint, request.CertificateSubject, request.CertificateExpiresAt);
        client.AssignScopes(assignedScopes);
        client.AssignRoles(assignedRoles);

        if (request.Active == false)
        {
            client.Deactivate();
        }

        await this._clientRepository.AddAsync(client, cancellationToken).ConfigureAwait(false);

        LogClientCreated(this._logger, client.Id, client.ClientId);

        return ToResponse(client);
    }

    public async Task<ClientResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await this._clientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        return ToResponse(client);
    }

    public async Task<ScimListResponse<ClientResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default)
    {
        string? clientIdFilter = null;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parsed = ScimFilterParser.Parse(filter);

            if (!string.Equals(parsed.AttributeName, "clientId", StringComparison.Ordinal))
            {
                throw new ValidationException(
                    $"Filter on '{parsed.AttributeName}' is not supported. Use 'clientId'.");
            }

            clientIdFilter = parsed.Value;
        }

        var clients = await this._clientRepository.ListAsync(clientIdFilter, cancellationToken).ConfigureAwait(false);
        var resources = clients.Select(ToResponse).ToList();

        return new ScimListResponse<ClientResponse>
        {
            TotalResults = resources.Count,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<ClientResponse> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateClientId(request.ClientId);
        var certificateThumbprint = NormalizeThumbprint(request.CertificateThumbprintSha256);
        var assignedScopes = AccessManagementValidation.ValidateNames(request.AssignedScopes, "assignedScopes");
        var assignedRoles = AccessManagementValidation.ValidateNames(request.AssignedRoles, "assignedRoles");
        await this.ValidateAssignmentsAsync(assignedScopes, assignedRoles, cancellationToken).ConfigureAwait(false);

        var client = await this._clientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        if (!string.Equals(client.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            if (await this._clientRepository.ExistsByClientIdAsync(request.ClientId!, cancellationToken).ConfigureAwait(false))
            {
                throw new ConflictException("clientId", request.ClientId!);
            }
        }

        client.Update(request.ClientId!, request.DisplayName, request.Active);
        client.UpdateCertificate(certificateThumbprint, request.CertificateSubject, request.CertificateExpiresAt);
        client.AssignScopes(assignedScopes);
        client.AssignRoles(assignedRoles);

        await this._clientRepository.UpdateAsync(client, cancellationToken).ConfigureAwait(false);

        LogClientUpdated(this._logger, client.Id);

        return ToResponse(client);
    }

    public async Task<ClientResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await this._clientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        foreach (var op in request.Operations)
        {
            ApplyPatchOperation(client, op);
        }

        await this.ValidateAssignmentsAsync(
            client.GetAssignedScopes(),
            client.GetAssignedRoles(),
            cancellationToken).ConfigureAwait(false);

        await this._clientRepository.UpdateAsync(client, cancellationToken).ConfigureAwait(false);

        LogClientPatched(this._logger, client.Id);

        return ToResponse(client);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await this._clientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        await this._clientRepository.DeleteAsync(client.Id, cancellationToken).ConfigureAwait(false);

        LogClientDeleted(this._logger, id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientCreated {ClientRecordId} {ClientId}")]
    private static partial void LogClientCreated(ILogger logger, Guid clientRecordId, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientUpdated {ClientRecordId}")]
    private static partial void LogClientUpdated(ILogger logger, Guid clientRecordId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientPatched {ClientRecordId}")]
    private static partial void LogClientPatched(ILogger logger, Guid clientRecordId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClientDeleted {ClientRecordId}")]
    private static partial void LogClientDeleted(ILogger logger, Guid clientRecordId);

    private static void ValidateClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ValidationException("clientId is required and must not be empty or whitespace.");
        }

        if (clientId.Length > 256)
        {
            throw new ValidationException("clientId must not exceed 256 characters.");
        }

        if (clientId.Contains('\0', StringComparison.Ordinal))
        {
            throw new ValidationException("clientId must not contain null characters.");
        }
    }

    private static void ApplyPatchOperation(MachineClient client, ScimPatchOperation op)
    {
        if (!string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Patch operation '{op.Op}' is not supported. Only 'replace' is supported.");
        }

        switch (op.Path.ToUpperInvariant())
        {
            case "DISPLAYNAME":
                ReplaceDisplayName(client, op.Value);
                break;

            case "ACTIVE":
                ReplaceActive(client, op.Value);
                break;

            case "CLIENTID":
                ReplaceClientId(client, op.Value);
                break;

            case "CERTIFICATETHUMBPRINTSHA256":
                ReplaceCertificateThumbprint(client, op.Value);
                break;

            case "CERTIFICATESUBJECT":
                ReplaceCertificateSubject(client, op.Value);
                break;

            case "CERTIFICATEEXPIRESAT":
                ReplaceCertificateExpiry(client, op.Value);
                break;

            case "ASSIGNEDSCOPES":
                ReplaceAssignedScopes(client, op.Value);
                break;

            case "ASSIGNEDROLES":
                ReplaceAssignedRoles(client, op.Value);
                break;

            default:
                throw new ValidationException($"Patch path '{op.Path}' is not supported.");
        }
    }

    private static ClientResponse ToResponse(MachineClient client)
    {
        return new ClientResponse
        {
            Id = client.Id.ToString(),
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            Active = client.Active,
            CertificateThumbprintSha256 = client.CertificateThumbprintSha256,
            CertificateSubject = client.CertificateSubject,
            CertificateExpiresAt = client.CertificateExpiresAt,
            AssignedScopes = client.GetAssignedScopes(),
            AssignedRoles = client.GetAssignedRoles(),
            Meta = new ScimMeta
            {
                ResourceType = "Client",
                Created = client.CreatedAt,
                LastModified = client.UpdatedAt,
                Location = $"/scim/v2/Clients/{client.Id}",
            },
        };
    }

    private static string? NormalizeThumbprint(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }

        var normalized = thumbprint.Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ValidationException("certificateThumbprintSha256 must be a 64-character hexadecimal SHA-256 thumbprint.");
        }

        return normalized;
    }

    private static List<string> ReadStringArray(JsonElement value, string fieldName)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException($"{fieldName} must be an array of strings.");
        }

        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ValidationException($"{fieldName} must be an array of strings.");
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static void ReplaceActive(MachineClient client, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new ValidationException("Value for 'active' must be a boolean.");
        }

        client.Update(client.ClientId, client.DisplayName, value.GetBoolean());
    }

    private static void ReplaceAssignedRoles(MachineClient client, JsonElement value)
    {
        client.AssignRoles(AccessManagementValidation.ValidateNames(ReadStringArray(value, "assignedRoles"), "assignedRoles"));
    }

    private static void ReplaceAssignedScopes(MachineClient client, JsonElement value)
    {
        client.AssignScopes(AccessManagementValidation.ValidateNames(ReadStringArray(value, "assignedScopes"), "assignedScopes"));
    }

    private static void ReplaceCertificateExpiry(MachineClient client, JsonElement value)
    {
        client.UpdateCertificate(
            client.CertificateThumbprintSha256,
            client.CertificateSubject,
            value.ValueKind == JsonValueKind.Null ? null : value.GetDateTimeOffset());
    }

    private static void ReplaceCertificateSubject(MachineClient client, JsonElement value)
    {
        client.UpdateCertificate(client.CertificateThumbprintSha256, value.GetString(), client.CertificateExpiresAt);
    }

    private static void ReplaceCertificateThumbprint(MachineClient client, JsonElement value)
    {
        client.UpdateCertificate(NormalizeThumbprint(value.GetString()), client.CertificateSubject, client.CertificateExpiresAt);
    }

    private static void ReplaceClientId(MachineClient client, JsonElement value)
    {
        var newClientId = value.GetString();
        ValidateClientId(newClientId);
        client.Update(newClientId!, client.DisplayName, client.Active);
    }

    private static void ReplaceDisplayName(MachineClient client, JsonElement value)
    {
        var displayName = value.ValueKind == JsonValueKind.Null
            ? null
            : value.GetString();
        client.Update(client.ClientId, displayName, client.Active);
    }

    private async Task ValidateAssignmentsAsync(
        IReadOnlyList<string> assignedScopes,
        IReadOnlyList<string> assignedRoles,
        CancellationToken cancellationToken)
    {
        await AccessManagementValidation
            .ValidateActiveScopesAsync(this._scopeRepository, assignedScopes, "assignedScopes", cancellationToken)
            .ConfigureAwait(false);
        await AccessManagementValidation
            .ValidateActiveRolesAsync(this._roleRepository, assignedRoles, "assignedRoles", cancellationToken)
            .ConfigureAwait(false);
    }
}
