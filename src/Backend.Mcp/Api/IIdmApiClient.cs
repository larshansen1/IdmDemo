using Backend.Application.Models.Auth;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Models.Users;

namespace Backend.Mcp.Api;

public interface IIdmApiClient
{
    Task<IdmApiCallResult<UserResponse>> CreateUserAsync(
        string? instance,
        CreateUserRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<UserResponse>> GetUserAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<UserResponse>> UpdateUserAsync(
        string? instance,
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<EmptyResponse>> DeleteUserAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ClientResponse>> CreateClientAsync(
        string? instance,
        CreateClientRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ClientResponse>> GetClientAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ScimListResponse<ClientResponse>>> ListClientsAsync(
        string? instance,
        string? filter,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ClientResponse>> UpdateClientAsync(
        string? instance,
        Guid id,
        UpdateClientRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<EmptyResponse>> DeleteClientAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<RoleResponse>> CreateRoleAsync(
        string? instance,
        CreateRoleRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<RoleResponse>> UpdateRoleAsync(
        string? instance,
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<EmptyResponse>> DeleteRoleAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ScopeResponse>> CreateScopeAsync(
        string? instance,
        CreateScopeRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ScopeResponse>> UpdateScopeAsync(
        string? instance,
        Guid id,
        UpdateScopeRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<EmptyResponse>> DeleteScopeAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<CertificateResponse>> CreateCertificateAsync(
        string? instance,
        Guid clientId,
        CreateCertificateRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<ScimListResponse<CertificateResponse>>> ListCertificatesAsync(
        string? instance,
        Guid clientId,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<CertificateResponse>> GetCertificateAsync(
        string? instance,
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<CertificateResponse>> RevokeCertificateAsync(
        string? instance,
        Guid clientId,
        Guid certificateId,
        RevokeCertificateRequest request,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<CertificateAuthorityResponse>> GetCertificateAuthorityAsync(
        string? instance,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<DiscoveryResponse>> GetDiscoveryAsync(
        string? instance,
        CancellationToken cancellationToken);

    Task<IdmApiCallResult<JwksResponse>> GetJwksAsync(
        string? instance,
        CancellationToken cancellationToken);
}
