using Backend.Application.Models.Auth;

namespace Backend.Application.Services;

public sealed class DpopBoundAccessTokenValidator : IDpopBoundAccessTokenValidator
{
    private const int _unauthorizedStatusCode = 401;

    private readonly IAccessTokenValidator _accessTokenValidator;
    private readonly IDpopProofValidator _dpopProofValidator;

    public DpopBoundAccessTokenValidator(
        IAccessTokenValidator accessTokenValidator,
        IDpopProofValidator dpopProofValidator)
    {
        ArgumentNullException.ThrowIfNull(accessTokenValidator);
        ArgumentNullException.ThrowIfNull(dpopProofValidator);
        this._accessTokenValidator = accessTokenValidator;
        this._dpopProofValidator = dpopProofValidator;
    }

    public async Task<ValidatedAccessToken> ValidateAsync(
        string accessToken,
        string dpopProofJwt,
        string httpMethod,
        Uri requestUri,
        CancellationToken cancellationToken = default)
    {
        var validatedToken = await this._accessTokenValidator
            .ValidateAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(validatedToken.DpopJwkThumbprint))
        {
            throw CreateInvalidTokenException();
        }

        var validatedProof = await this._dpopProofValidator
            .ValidateProtectedResourceProofAsync(
                dpopProofJwt,
                accessToken,
                httpMethod,
                requestUri,
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(
            validatedToken.DpopJwkThumbprint,
            validatedProof.JwkThumbprint,
            StringComparison.Ordinal))
        {
            throw CreateInvalidTokenException();
        }

        return validatedToken;
    }

    private static OAuthException CreateInvalidTokenException()
    {
        return new OAuthException("invalid_token", "Access token is missing or invalid.", _unauthorizedStatusCode);
    }
}
