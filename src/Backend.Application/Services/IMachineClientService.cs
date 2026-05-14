using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;

namespace Backend.Application.Services;

public interface IMachineClientService
{
    Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default);

    Task<ClientResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ScimListResponse<ClientResponse>> ListAsync(string? filter, CancellationToken cancellationToken = default);

    Task<ClientResponse> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default);

    Task<ClientResponse> PatchAsync(Guid id, ScimPatchRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
