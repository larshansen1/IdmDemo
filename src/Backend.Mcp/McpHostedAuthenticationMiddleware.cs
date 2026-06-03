using System.Security.Claims;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpHostedAuthenticationMiddleware
{
    private const string _authorizationHeader = "Authorization";
    private const string _dpopHeader = "DPoP";

    private readonly RequestDelegate _next;

    public McpHostedAuthenticationMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        this._next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<McpRuntimeOptions> options,
        IAccessTokenValidator accessTokenValidator,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accessTokenValidator);
        ArgumentNullException.ThrowIfNull(dpopBoundAccessTokenValidator);

        if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal))
        {
            await this._next(context).ConfigureAwait(false);
            return;
        }

        var authorization = ReadHeader(context, _authorizationHeader);
        if (!TryReadAuthorization(authorization, out var scheme, out var accessToken))
        {
            await WriteUnauthorizedAsync(context).ConfigureAwait(false);
            return;
        }

        try
        {
            var runtimeSettings = McpRuntimeProfileResolver.Resolve(options.Value);
            var validatedToken = runtimeSettings.RequireDpop
                ? await ValidateDpopAsync(context, scheme, accessToken, dpopBoundAccessTokenValidator).ConfigureAwait(false)
                : await ValidateBearerDevelopmentAsync(
                    context,
                    scheme,
                    accessToken,
                    accessTokenValidator,
                    dpopBoundAccessTokenValidator,
                    runtimeSettings)
                    .ConfigureAwait(false);

            context.User = CreatePrincipal(validatedToken, scheme);
            await this._next(context).ConfigureAwait(false);
        }
        catch (OAuthException)
        {
            await WriteUnauthorizedAsync(context).ConfigureAwait(false);
        }
    }

    private static async Task<ValidatedAccessToken> ValidateDpopAsync(
        HttpContext context,
        string scheme,
        string accessToken,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator)
    {
        if (!string.Equals(scheme, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException("invalid_token", "DPoP-bound access token is required.", StatusCodes.Status401Unauthorized);
        }

        var proof = ReadHeader(context, _dpopHeader);
        if (string.IsNullOrWhiteSpace(proof))
        {
            throw new OAuthException("invalid_dpop_proof", "DPoP proof is required.", StatusCodes.Status401Unauthorized);
        }

        return await dpopBoundAccessTokenValidator
            .ValidateAsync(accessToken, proof, context.Request.Method, CreateRequestUri(context.Request), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task<ValidatedAccessToken> ValidateBearerDevelopmentAsync(
        HttpContext context,
        string scheme,
        string accessToken,
        IAccessTokenValidator accessTokenValidator,
        IDpopBoundAccessTokenValidator dpopBoundAccessTokenValidator,
        McpEffectiveRuntimeSettings runtimeSettings)
    {
        if (string.Equals(scheme, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            var proof = ReadHeader(context, _dpopHeader);
            if (string.IsNullOrWhiteSpace(proof))
            {
                throw new OAuthException("invalid_dpop_proof", "DPoP proof is required.", StatusCodes.Status401Unauthorized);
            }

            return await dpopBoundAccessTokenValidator
                .ValidateAsync(accessToken, proof, context.Request.Method, CreateRequestUri(context.Request), context.RequestAborted)
                .ConfigureAwait(false);
        }

        if (!runtimeSettings.AllowBearerTokensForDevelopment ||
            !string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException("invalid_token", "Access token is missing or invalid.", StatusCodes.Status401Unauthorized);
        }

        var validated = await accessTokenValidator.ValidateAsync(accessToken, context.RequestAborted).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(validated.DpopJwkThumbprint))
        {
            throw new OAuthException(
                "invalid_token",
                "DPoP-bound access token must be presented with a DPoP proof, not as a plain Bearer.",
                StatusCodes.Status401Unauthorized);
        }

        return validated;
    }

    private static ClaimsPrincipal CreatePrincipal(ValidatedAccessToken token, string scheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.Subject),
            new("sub", token.Subject),
            new("client_id", token.ClientId),
        };

        claims.AddRange(token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(scope => new Claim("scope", scope)));

        if (!string.IsNullOrWhiteSpace(token.DpopJwkThumbprint))
        {
            claims.Add(new Claim("cnf_jkt", token.DpopJwkThumbprint));
        }

        if (!string.IsNullOrWhiteSpace(token.CertificateThumbprintSha256))
        {
            claims.Add(new Claim("cnf_x5t_s256", token.CertificateThumbprintSha256));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    private static bool TryReadAuthorization(string? authorization, out string scheme, out string accessToken)
    {
        scheme = string.Empty;
        accessToken = string.Empty;

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        var separator = authorization.IndexOf(' ', StringComparison.Ordinal);
        if (separator <= 0 || separator == authorization.Length - 1)
        {
            return false;
        }

        scheme = authorization[..separator];
        accessToken = authorization[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(accessToken);
    }

    private static string? ReadHeader(HttpContext context, string name)
    {
        return context.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
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

    private static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "DPoP, Bearer";
        return Task.CompletedTask;
    }
}
