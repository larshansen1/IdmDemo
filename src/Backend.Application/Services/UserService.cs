using System.Text.Json;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Users;
using Backend.Application.Scim;
using Backend.Domain.Entities;
using Backend.Domain.Exceptions;
using Backend.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Services;

public sealed partial class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IGlobalRoleRepository _roleRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IGlobalRoleRepository roleRepository,
        ILogger<UserService> logger)
    {
        this._userRepository = userRepository;
        this._roleRepository = roleRepository;
        this._logger = logger;
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUserName(request.UserName);
        var assignedRoles = AccessManagementValidation.ValidateNames(request.AssignedRoles, "assignedRoles");
        await AccessManagementValidation
            .ValidateActiveRolesAsync(this._roleRepository, assignedRoles, "assignedRoles", cancellationToken)
            .ConfigureAwait(false);

        if (await this._userRepository.ExistsByUserNameAsync(request.UserName!, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("userName", request.UserName!);
        }

        var user = User.Create(request.UserName!, request.DisplayName, request.ExternalId);
        user.AssignRoles(assignedRoles);

        if (request.Active == false)
        {
            user.Deactivate();
        }

        await this._userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);

        LogUserCreated(this._logger, user.Id, user.UserName);

        return ToResponse(user);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await this._userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("User", id.ToString());

        return ToResponse(user);
    }

    public async Task<ScimListResponse<UserResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default)
    {
        string? userNameFilter = null;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parsed = ScimFilterParser.Parse(filter);

            if (!string.Equals(parsed.AttributeName, "userName", StringComparison.Ordinal))
            {
                throw new ValidationException(
                    $"Filter on '{parsed.AttributeName}' is not supported. Use 'userName'.");
            }

            userNameFilter = parsed.Value;
        }

        var users = await this._userRepository.ListAsync(userNameFilter, cancellationToken).ConfigureAwait(false);
        var resources = users.Select(ToResponse).ToList();

        return new ScimListResponse<UserResponse>
        {
            TotalResults = resources.Count,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUserName(request.UserName);
        var assignedRoles = AccessManagementValidation.ValidateNames(request.AssignedRoles, "assignedRoles");
        await AccessManagementValidation
            .ValidateActiveRolesAsync(this._roleRepository, assignedRoles, "assignedRoles", cancellationToken)
            .ConfigureAwait(false);

        var user = await this._userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("User", id.ToString());

        if (!string.Equals(user.UserName, request.UserName, StringComparison.Ordinal))
        {
            if (await this._userRepository.ExistsByUserNameAsync(request.UserName!, cancellationToken).ConfigureAwait(false))
            {
                throw new ConflictException("userName", request.UserName!);
            }
        }

        user.Update(request.UserName!, request.DisplayName, request.ExternalId, request.Active);
        user.AssignRoles(assignedRoles);

        await this._userRepository.UpdateAsync(user, cancellationToken).ConfigureAwait(false);

        LogUserUpdated(this._logger, user.Id);

        return ToResponse(user);
    }

    public async Task<UserResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await this._userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("User", id.ToString());

        foreach (var op in request.Operations)
        {
            ApplyPatchOperation(user, op);
        }

        await AccessManagementValidation
            .ValidateActiveRolesAsync(this._roleRepository, user.GetAssignedRoles(), "assignedRoles", cancellationToken)
            .ConfigureAwait(false);

        await this._userRepository.UpdateAsync(user, cancellationToken).ConfigureAwait(false);

        LogUserPatched(this._logger, user.Id);

        return ToResponse(user);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await this._userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("User", id.ToString());

        await this._userRepository.DeleteAsync(user.Id, cancellationToken).ConfigureAwait(false);

        LogUserDeleted(this._logger, id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "UserCreated {UserId} {UserName}")]
    private static partial void LogUserCreated(ILogger logger, Guid userId, string userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "UserUpdated {UserId}")]
    private static partial void LogUserUpdated(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "UserPatched {UserId}")]
    private static partial void LogUserPatched(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "UserDeleted {UserId}")]
    private static partial void LogUserDeleted(ILogger logger, Guid userId);

    private static void ValidateUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ValidationException("userName is required and must not be empty or whitespace.");
        }

        if (userName.Length > 256)
        {
            throw new ValidationException("userName must not exceed 256 characters.");
        }

        if (userName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ValidationException("userName must not contain null characters.");
        }
    }

    private static void ApplyPatchOperation(User user, ScimPatchOperation op)
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
                user.Update(user.UserName, displayName, user.ExternalId, user.Active);
                break;

            case "ACTIVE":
                if (op.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new ValidationException("Value for 'active' must be a boolean.");
                }

                user.Update(user.UserName, user.DisplayName, user.ExternalId, op.Value.GetBoolean());
                break;

            case "USERNAME":
                var newUserName = op.Value.GetString();
                ValidateUserName(newUserName);
                user.Update(newUserName!, user.DisplayName, user.ExternalId, user.Active);
                break;

            case "ASSIGNEDROLES":
                user.AssignRoles(AccessManagementValidation.ValidateNames(ReadStringArray(op.Value, "assignedRoles"), "assignedRoles"));
                break;

            default:
                throw new ValidationException($"Patch path '{op.Path}' is not supported.");
        }
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

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id.ToString(),
            ExternalId = user.ExternalId,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            Active = user.Active,
            AssignedRoles = user.GetAssignedRoles(),
            Meta = new ScimMeta
            {
                ResourceType = "User",
                Created = user.CreatedAt,
                LastModified = user.UpdatedAt,
                Location = $"/scim/v2/Users/{user.Id}",
            },
        };
    }
}
