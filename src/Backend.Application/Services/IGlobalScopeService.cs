using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;

namespace Backend.Application.Services;

public interface IGlobalScopeService
{
    Task<ScopeResponse> CreateAsync(CreateScopeRequest request, CancellationToken cancellationToken = default);

    Task<ScopeResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ScimListResponse<ScopeResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default);

    Task<ScopeResponse> UpdateAsync(Guid id, UpdateScopeRequest request, CancellationToken cancellationToken = default);

    Task<ScopeResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
