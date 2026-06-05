using System.ComponentModel;
using Backend.Application.Models.Users;
using Backend.Mcp.Api;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmUserTools
{
    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;

    public IdmUserTools(IIdmApiClient apiClient, IMcpMutationGuard mutationGuard)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
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
        return new OperationResult(result.Instance, result.CorrelationId, IdmToolHelpers.Succeeded);
    }
}
