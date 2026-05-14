namespace Backend.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string _correlationIdHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        this._next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var correlationId = context.Request.Headers.TryGetValue(_correlationIdHeader, out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[_correlationIdHeader] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await this._next(context).ConfigureAwait(false);
        }
    }
}
