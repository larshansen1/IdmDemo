using System.Text.Json;
using Backend.Application.Models.Scim;
using Backend.Application.Services;

namespace Backend.Api.Middleware;

public sealed class ScimOAuthMiddleware
{
    private const string _bearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;

    public ScimOAuthMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        this._next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAccessTokenValidator accessTokenValidator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accessTokenValidator);

        if (IsPublicPath(context.Request.Path))
        {
            await this._next(context).ConfigureAwait(false);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(_bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, "Bearer token is required.").ConfigureAwait(false);
            return;
        }

        var accessToken = authorization[_bearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await WriteUnauthorizedAsync(context, "Bearer token is required.").ConfigureAwait(false);
            return;
        }

        try
        {
            var validated = await accessTokenValidator.ValidateAsync(accessToken, context.RequestAborted).ConfigureAwait(false);
            if (!validated.Roles.Contains(ScimAdminRoles.Admin, StringComparer.Ordinal))
            {
                await WriteForbiddenAsync(context).ConfigureAwait(false);
                return;
            }
        }
        catch (OAuthException)
        {
            await WriteUnauthorizedAsync(context, "Bearer token is invalid or expired.").ConfigureAwait(false);
            return;
        }

        await this._next(context).ConfigureAwait(false);
    }

    private static bool IsPublicPath(PathString path)
    {
        return path.StartsWithSegments("/.well-known", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/connect/token", StringComparison.OrdinalIgnoreCase);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        context.Response.Headers.WWWAuthenticate = "Bearer";
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
