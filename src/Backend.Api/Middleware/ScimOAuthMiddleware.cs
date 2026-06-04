using System.Security.Claims;
using System.Text.Json;
using Backend.Api.Composition;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Scim;
using Backend.Application.Services;

namespace Backend.Api.Middleware;

public sealed class ScimOAuthMiddleware
{
    private const string _bearerPrefix = "Bearer ";
    private const string _dpopPrefix = "DPoP ";

    private readonly RequestDelegate _next;

    public ScimOAuthMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        this._next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AuthorizationServerOptions options,
        IAccessTokenValidator accessTokenValidator,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accessTokenValidator);
        ArgumentNullException.ThrowIfNull(dpopBoundAccessTokenValidator);

        if (IsPublicPath(context.Request.Path))
        {
            await this._next(context).ConfigureAwait(false);
            return;
        }

        if (!TryReadAuthorization(context.Request.Headers.Authorization.ToString(), out var scheme, out var accessToken))
        {
            await WriteUnauthorizedAsync(context, "Access token is required.").ConfigureAwait(false);
            return;
        }

        try
        {
            var validated = options.RequireDpop
                ? await ValidateDpopAsync(context, scheme, accessToken, dpopBoundAccessTokenValidator).ConfigureAwait(false)
                : await ValidateBearerOrDpopAsync(
                    context,
                    scheme,
                    accessToken,
                    accessTokenValidator,
                    dpopBoundAccessTokenValidator)
                    .ConfigureAwait(false);

            if (!validated.Roles.Contains(ScimAdminRoles.Admin, StringComparer.Ordinal))
            {
                await WriteForbiddenAsync(context).ConfigureAwait(false);
                return;
            }

            context.User = CreatePrincipal(validated);
        }
        catch (OAuthException)
        {
            await WriteUnauthorizedAsync(context, "Bearer token is invalid or expired.").ConfigureAwait(false);
            return;
        }

        await this._next(context).ConfigureAwait(false);
    }

    private static ClaimsPrincipal CreatePrincipal(ValidatedAccessToken token)
    {
        var claims = new List<Claim>
        {
            new("sub", token.Subject),
            new("client_id", token.ClientId),
        };

        if (!string.IsNullOrWhiteSpace(token.Scope))
        {
            claims.Add(new Claim("scope", token.Scope));
        }

        foreach (var role in token.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static async Task<ValidatedAccessToken> ValidateBearerOrDpopAsync(
        HttpContext context,
        string scheme,
        string accessToken,
        IAccessTokenValidator accessTokenValidator,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator)
    {
        if (string.Equals(scheme, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            return await ValidateDpopAsync(context, scheme, accessToken, dpopBoundAccessTokenValidator).ConfigureAwait(false);
        }

        if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidToken();
        }

        var validated = await accessTokenValidator.ValidateAsync(accessToken, context.RequestAborted).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(validated.DpopJwkThumbprint))
        {
            ThrowInvalidToken();
        }

        return validated;
    }

    private static async Task<ValidatedAccessToken> ValidateDpopAsync(
        HttpContext context,
        string scheme,
        string accessToken,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator)
    {
        if (!string.Equals(scheme, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidToken();
        }

        var proof = context.Request.Headers["DPoP"].ToString();
        if (string.IsNullOrWhiteSpace(proof))
        {
            ThrowInvalidDpopProof();
        }

        return await dpopBoundAccessTokenValidator
            .ValidateAsync(accessToken, proof, context.Request.Method, CreateRequestUri(context.Request), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static bool IsPublicPath(PathString path)
    {
        return AuthorizationServerApiComposition.IsAuthorizationServerRoute(path);
    }

    private static bool TryReadAuthorization(string? authorization, out string scheme, out string accessToken)
    {
        scheme = string.Empty;
        accessToken = string.Empty;

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        if (authorization.StartsWith(_bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            scheme = "Bearer";
            accessToken = authorization[_bearerPrefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(accessToken);
        }

        if (authorization.StartsWith(_dpopPrefix, StringComparison.OrdinalIgnoreCase))
        {
            scheme = "DPoP";
            accessToken = authorization[_dpopPrefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(accessToken);
        }

        return false;
    }

    private static Uri CreateRequestUri(HttpRequest request)
    {
        var builder = new UriBuilder
        {
            Scheme = request.Scheme,
            Host = request.Host.Host,
            Path = $"{request.PathBase}{request.Path}",
            Query = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty,
        };

        if (request.Host.Port is { } port)
        {
            builder.Port = port;
        }

        return builder.Uri;
    }

    private static void ThrowInvalidToken()
    {
        throw new OAuthException("invalid_token", "Access token is missing or invalid.", StatusCodes.Status401Unauthorized);
    }

    private static void ThrowInvalidDpopProof()
    {
        throw new OAuthException("invalid_dpop_proof", "DPoP proof is required.", StatusCodes.Status401Unauthorized);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        context.Response.Headers.WWWAuthenticate = "DPoP, Bearer";
        var error = new ScimError { Status = StatusCodes.Status401Unauthorized, Detail = detail };
        return context.Response.WriteAsync(JsonSerializer.Serialize(error));
    }

    private static Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        var error = new ScimError
        {
            Status = StatusCodes.Status403Forbidden,
            Detail = $"Access token does not have the required '{ScimAdminRoles.Admin}' role.",
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(error));
    }
}
