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
    private readonly ILogger<MachineClientService> _logger;

    public MachineClientService(IMachineClientRepository clientRepository, ILogger<MachineClientService> logger)
    {
        this._clientRepository = clientRepository;
        this._logger = logger;
    }

    public async Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateClientId(request.ClientId);

        if (await this._clientRepository.ExistsByClientIdAsync(request.ClientId!, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("clientId", request.ClientId!);
        }

        var client = MachineClient.Create(request.ClientId!, request.DisplayName);

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
                var displayName = op.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : op.Value.GetString();
                client.Update(client.ClientId, displayName, client.Active);
                break;

            case "ACTIVE":
                if (op.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new ValidationException("Value for 'active' must be a boolean.");
                }

                client.Update(client.ClientId, client.DisplayName, op.Value.GetBoolean());
                break;

            case "CLIENTID":
                var newClientId = op.Value.GetString();
                ValidateClientId(newClientId);
                client.Update(newClientId!, client.DisplayName, client.Active);
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
            Meta = new ScimMeta
            {
                ResourceType = "Client",
                Created = client.CreatedAt,
                LastModified = client.UpdatedAt,
                Location = $"/scim/v2/Clients/{client.Id}",
            },
        };
    }
}
