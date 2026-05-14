using Backend.Application.Models.Certificates;
using Backend.Application.Models.Scim;

namespace Backend.Application.Services;

public interface IMachineClientCertificateService
{
    Task<CertificateResponse> CreateAsync(
        Guid clientId,
        CreateCertificateRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateResponse> GetAsync(
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken = default);

    Task<ScimListResponse<CertificateResponse>> ListAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);

    Task<CertificateResponse> RevokeAsync(
        Guid clientId,
        Guid certificateId,
        RevokeCertificateRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateAuthorityResponse> GetCertificateAuthorityAsync(CancellationToken cancellationToken = default);
}
