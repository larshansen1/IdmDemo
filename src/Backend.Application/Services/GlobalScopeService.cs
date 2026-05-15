using System.Text.Json;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Scim;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class GlobalScopeService : IGlobalScopeService
{
    private readonly IGlobalScopeRepository _scopeRepository;
    private readonly IMachineClientRepository _clientRepository;
    private readonly ILogger<GlobalScopeService> _logger;

    public GlobalScopeService(
        IGlobalScopeRepository scopeRepository,
        IMachineClientRepository clientRepository,
        ILogger<GlobalScopeService> logger)
    {
        this._scopeRepository = scopeRepository;
        this._clientRepository = clientRepository;
        this._logger = logger;
    }

    public async Task<ScopeResponse> CreateAsync(CreateScopeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = AccessManagementValidation.ValidateValue(request.Value, "value");

        if (await this._scopeRepository.ExistsByValueAsync(value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", value);
        }

        var scope = GlobalScope.Create(value, request.DisplayName, request.Description);
        if (request.Active == false)
        {
            scope.Deactivate();
        }

        await this._scopeRepository.AddAsync(scope, cancellationToken).ConfigureAwait(false);
        LogScopeCreated(this._logger, scope.Id, scope.Value);

        return ToResponse(scope);
    }

    public async Task<ScopeResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await this._scopeRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Scope", id.ToString());

        return ToResponse(scope);
    }

    public async Task<ScimListResponse<ScopeResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var valueFilter = ParseValueFilter(filter);
        var scopes = await this._scopeRepository.ListAsync(valueFilter, cancellationToken).ConfigureAwait(false);
        var resources = scopes.Select(ToResponse).ToList();

        return new ScimListResponse<ScopeResponse>
        {
            TotalResults = resources.Count,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<ScopeResponse> UpdateAsync(Guid id, UpdateScopeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = AccessManagementValidation.ValidateValue(request.Value, "value");

        var scope = await this._scopeRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Scope", id.ToString());

        if (!string.Equals(scope.Value, value, StringComparison.Ordinal)
            && await this._scopeRepository.ExistsByValueAsync(value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", value);
        }

        scope.Update(value, request.DisplayName, request.Description, request.Active);
        await this._scopeRepository.UpdateAsync(scope, cancellationToken).ConfigureAwait(false);
        LogScopeUpdated(this._logger, scope.Id);

        return ToResponse(scope);
    }

    public async Task<ScopeResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await this._scopeRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Scope", id.ToString());

        var originalValue = scope.Value;
        foreach (var op in request.Operations)
        {
            ApplyPatchOperation(scope, op);
        }

        if (!string.Equals(originalValue, scope.Value, StringComparison.Ordinal)
            && await this._scopeRepository.ExistsByValueAsync(scope.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", scope.Value);
        }

        await this._scopeRepository.UpdateAsync(scope, cancellationToken).ConfigureAwait(false);
        LogScopePatched(this._logger, scope.Id);

        return ToResponse(scope);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await this._scopeRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Scope", id.ToString());

        if (await this.IsAssignedAsync(scope.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", scope.Value);
        }

        await this._scopeRepository.DeleteAsync(scope.Id, cancellationToken).ConfigureAwait(false);
        LogScopeDeleted(this._logger, id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ScopeCreated {ScopeId} {ScopeValue}")]
    private static partial void LogScopeCreated(ILogger logger, Guid scopeId, string scopeValue);

    [LoggerMessage(Level = LogLevel.Information, Message = "ScopeUpdated {ScopeId}")]
    private static partial void LogScopeUpdated(ILogger logger, Guid scopeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ScopePatched {ScopeId}")]
    private static partial void LogScopePatched(ILogger logger, Guid scopeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ScopeDeleted {ScopeId}")]
    private static partial void LogScopeDeleted(ILogger logger, Guid scopeId);

    private static string? ParseValueFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var parsed = ScimFilterParser.Parse(filter);
        if (!string.Equals(parsed.AttributeName, "value", StringComparison.Ordinal))
        {
            throw new ValidationException($"Filter on '{parsed.AttributeName}' is not supported. Use 'value'.");
        }

        return parsed.Value;
    }

    private static void ApplyPatchOperation(GlobalScope scope, ScimPatchOperation op)
    {
        if (!string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Patch operation '{op.Op}' is not supported. Only 'replace' is supported.");
        }

        switch (op.Path.ToUpperInvariant())
        {
            case "VALUE":
                scope.Update(
                    AccessManagementValidation.ValidateValue(op.Value.GetString(), "value"),
                    scope.DisplayName,
                    scope.Description,
                    scope.Active);
                break;

            case "DISPLAYNAME":
                scope.Update(scope.Value, ReadNullableString(op.Value), scope.Description, scope.Active);
                break;

            case "DESCRIPTION":
                scope.Update(scope.Value, scope.DisplayName, ReadNullableString(op.Value), scope.Active);
                break;

            case "ACTIVE":
                if (op.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new ValidationException("Value for 'active' must be a boolean.");
                }

                scope.Update(scope.Value, scope.DisplayName, scope.Description, op.Value.GetBoolean());
                break;

            default:
                throw new ValidationException($"Patch path '{op.Path}' is not supported.");
        }
    }

    private static string? ReadNullableString(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static ScopeResponse ToResponse(GlobalScope scope)
    {
        return new ScopeResponse
        {
            Id = scope.Id.ToString(),
            Value = scope.Value,
            DisplayName = scope.DisplayName,
            Description = scope.Description,
            Active = scope.Active,
            Meta = new ScimMeta
            {
                ResourceType = "Scope",
                Created = scope.CreatedAt,
                LastModified = scope.UpdatedAt,
                Location = $"/scim/v2/Scopes/{scope.Id}",
            },
        };
    }

    private async Task<bool> IsAssignedAsync(string value, CancellationToken cancellationToken)
    {
        var clients = await this._clientRepository.ListAsync(null, cancellationToken).ConfigureAwait(false);
        return clients.Any(c => c.GetAssignedScopes().Contains(value, StringComparer.Ordinal));
    }
}
