using Backend.Application.Models.Scim;
using Backend.Application.Models.Users;

namespace Backend.Application.Services;

public interface IUserService
{
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ScimListResponse<UserResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default);

    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<UserResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
