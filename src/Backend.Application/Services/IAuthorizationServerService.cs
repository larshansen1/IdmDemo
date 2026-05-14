using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Auth;

namespace Backend.Application.Services;

public interface IAuthorizationServerService
{
    DiscoveryResponse GetDiscovery();

    Task<JwksResponse> GetJwksAsync(CancellationToken cancellationToken = default);

    Task<TokenResponse> IssueClientCredentialsTokenAsync(
        string? grantType,
        string? clientId,
        string? scope,
        X509Certificate2? clientCertificate,
        CancellationToken cancellationToken = default);
}
