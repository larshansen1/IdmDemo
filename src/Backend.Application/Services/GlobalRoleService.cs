using System.Text.Json;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Scim;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class GlobalRoleService : IGlobalRoleService
{
    private readonly IGlobalRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMachineClientRepository _clientRepository;
    private readonly ILogger<GlobalRoleService> _logger;

    public GlobalRoleService(
        IGlobalRoleRepository roleRepository,
        IUserRepository userRepository,
        IMachineClientRepository clientRepository,
        ILogger<GlobalRoleService> logger)
    {
        this._roleRepository = roleRepository;
        this._userRepository = userRepository;
        this._clientRepository = clientRepository;
        this._logger = logger;
    }

    public async Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = AccessManagementValidation.ValidateValue(request.Value, "value");

        if (await this._roleRepository.ExistsByValueAsync(value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", value);
        }

        var role = GlobalRole.Create(value, request.DisplayName, request.Description);
        if (request.Active == false)
        {
            role.Deactivate();
        }

        await this._roleRepository.AddAsync(role, cancellationToken).ConfigureAwait(false);
        LogRoleCreated(this._logger, role.Id, role.Value);

        return ToResponse(role);
    }

    public async Task<RoleResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await this._roleRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Role", id.ToString());

        return ToResponse(role);
    }

    public async Task<ScimListResponse<RoleResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var valueFilter = ParseValueFilter(filter);
        var roles = await this._roleRepository.ListAsync(valueFilter, cancellationToken).ConfigureAwait(false);
        var resources = roles.Select(ToResponse).ToList();

        return new ScimListResponse<RoleResponse>
        {
            TotalResults = resources.Count,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<RoleResponse> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = AccessManagementValidation.ValidateValue(request.Value, "value");

        var role = await this._roleRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Role", id.ToString());

        if (!string.Equals(role.Value, value, StringComparison.Ordinal)
            && await this._roleRepository.ExistsByValueAsync(value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", value);
        }

        role.Update(value, request.DisplayName, request.Description, request.Active);
        await this._roleRepository.UpdateAsync(role, cancellationToken).ConfigureAwait(false);
        LogRoleUpdated(this._logger, role.Id);

        return ToResponse(role);
    }

    public async Task<RoleResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await this._roleRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Role", id.ToString());

        var originalValue = role.Value;
        foreach (var op in request.Operations)
        {
            ApplyPatchOperation(role, op);
        }

        if (!string.Equals(originalValue, role.Value, StringComparison.Ordinal)
            && await this._roleRepository.ExistsByValueAsync(role.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", role.Value);
        }

        await this._roleRepository.UpdateAsync(role, cancellationToken).ConfigureAwait(false);
        LogRolePatched(this._logger, role.Id);

        return ToResponse(role);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await this._roleRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Role", id.ToString());

        if (await this.IsAssignedAsync(role.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("value", role.Value);
        }

        await this._roleRepository.DeleteAsync(role.Id, cancellationToken).ConfigureAwait(false);
        LogRoleDeleted(this._logger, id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RoleCreated {RoleId} {RoleValue}")]
    private static partial void LogRoleCreated(ILogger logger, Guid roleId, string roleValue);

    [LoggerMessage(Level = LogLevel.Information, Message = "RoleUpdated {RoleId}")]
    private static partial void LogRoleUpdated(ILogger logger, Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "RolePatched {RoleId}")]
    private static partial void LogRolePatched(ILogger logger, Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "RoleDeleted {RoleId}")]
    private static partial void LogRoleDeleted(ILogger logger, Guid roleId);

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

    private static void ApplyPatchOperation(GlobalRole role, ScimPatchOperation op)
    {
        if (!string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Patch operation '{op.Op}' is not supported. Only 'replace' is supported.");
        }

        switch (op.Path.ToUpperInvariant())
        {
            case "VALUE":
                role.Update(
                    AccessManagementValidation.ValidateValue(op.Value.GetString(), "value"),
                    role.DisplayName,
                    role.Description,
                    role.Active);
                break;

            case "DISPLAYNAME":
                role.Update(role.Value, ReadNullableString(op.Value), role.Description, role.Active);
                break;

            case "DESCRIPTION":
                role.Update(role.Value, role.DisplayName, ReadNullableString(op.Value), role.Active);
                break;

            case "ACTIVE":
                if (op.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new ValidationException("Value for 'active' must be a boolean.");
                }

                role.Update(role.Value, role.DisplayName, role.Description, op.Value.GetBoolean());
                break;

            default:
                throw new ValidationException($"Patch path '{op.Path}' is not supported.");
        }
    }

    private static string? ReadNullableString(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static RoleResponse ToResponse(GlobalRole role)
    {
        return new RoleResponse
        {
            Id = role.Id.ToString(),
            Value = role.Value,
            DisplayName = role.DisplayName,
            Description = role.Description,
            Active = role.Active,
            Meta = new ScimMeta
            {
                ResourceType = "Role",
                Created = role.CreatedAt,
                LastModified = role.UpdatedAt,
                Location = $"/scim/v2/Roles/{role.Id}",
            },
        };
    }

    private async Task<bool> IsAssignedAsync(string value, CancellationToken cancellationToken)
    {
        var users = await this._userRepository.ListAsync(null, cancellationToken).ConfigureAwait(false);
        if (users.Any(u => u.GetAssignedRoles().Contains(value, StringComparer.Ordinal)))
        {
            return true;
        }

        var clients = await this._clientRepository.ListAsync(null, cancellationToken).ConfigureAwait(false);
        return clients.Any(c => c.GetAssignedRoles().Contains(value, StringComparer.Ordinal));
    }
}
