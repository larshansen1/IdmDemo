using Backend.Application.Models.Auth;

namespace Backend.Application.Services;

public interface IAccessTokenValidator
{
    Task<ValidatedAccessToken> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
