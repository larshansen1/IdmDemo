using Backend.Application.Models.Auth;

namespace Backend.Application.Services;

public interface IDpopProofValidator
{
    Task<ValidatedDpopProof> ValidateTokenEndpointProofAsync(
        string proofJwt,
        Uri expectedTokenEndpoint,
        CancellationToken cancellationToken = default);

    Task<ValidatedDpopProof> ValidateProtectedResourceProofAsync(
        string proofJwt,
        string accessToken,
        string httpMethod,
        Uri expectedResourceUri,
        CancellationToken cancellationToken = default);
}
