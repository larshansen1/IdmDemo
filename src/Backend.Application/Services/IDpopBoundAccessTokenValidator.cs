using Backend.Application.Models.Auth;

namespace Backend.Application.Services;

public interface IDpopBoundAccessTokenValidator
{
    Task<ValidatedAccessToken> ValidateAsync(
        string accessToken,
        string dpopProofJwt,
        string httpMethod,
        Uri requestUri,
        CancellationToken cancellationToken = default);
}
