using System.Text.Json;
using Backend.Application.Models.Scim;

namespace Backend.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private const string _apiKeyHeader = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this._next = next;
        this._apiKey = configuration["AdminApi:ApiKey"]
            ?? throw new InvalidOperationException("AdminApi:ApiKey configuration is required.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.StartsWithSegments("/.well-known", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/connect/token", StringComparison.OrdinalIgnoreCase))
        {
            await this._next(context).ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_apiKeyHeader, out var extractedKey) ||
            !string.Equals(extractedKey.ToString(), this._apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var error = new ScimError
            {
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Missing or invalid API key.",
            };

            await context.Response
                .WriteAsync(JsonSerializer.Serialize(error))
                .ConfigureAwait(false);

            return;
        }

        await this._next(context).ConfigureAwait(false);
    }
}
