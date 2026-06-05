using System.ComponentModel;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scopes;
using Backend.Mcp.Api;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Tools;

[McpServerToolType]
public sealed class IdmCatalogTools
{
    private readonly IIdmApiClient _apiClient;
    private readonly IMcpMutationGuard _mutationGuard;

    public IdmCatalogTools(IIdmApiClient apiClient, IMcpMutationGuard mutationGuard)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mutationGuard);

        this._apiClient = apiClient;
        this._mutationGuard = mutationGuard;
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
        return new OperationResult(result.Instance, result.CorrelationId, IdmToolHelpers.Succeeded);
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
        return new OperationResult(result.Instance, result.CorrelationId, IdmToolHelpers.Succeeded);
    }
}
