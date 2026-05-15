using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;

namespace Backend.Application.Services;

public interface IGlobalRoleService
{
    Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);

    Task<RoleResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ScimListResponse<RoleResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default);

    Task<RoleResponse> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default);

    Task<RoleResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
