namespace Backend.Api.Middleware;

public sealed partial class ScimAdminAuditMiddleware
{
    private const string _correlationIdHeader = "X-Correlation-Id";

    private static readonly HashSet<string> _mutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Delete,
        HttpMethods.Patch,
        HttpMethods.Post,
        HttpMethods.Put,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ScimAdminAuditMiddleware> _logger;

    public ScimAdminAuditMiddleware(RequestDelegate next, ILogger<ScimAdminAuditMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);

        this._next = next;
        this._logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await this._next(context).ConfigureAwait(false);

        if (ScimAdminAuditMiddleware.IsScimMutation(context.Request))
        {
            this.LogAudit(context);
        }
    }

    private static bool IsScimMutation(HttpRequest request)
    {
        return _mutationMethods.Contains(request.Method)
            && request.Path.StartsWithSegments("/scim/v2", StringComparison.OrdinalIgnoreCase);
    }

    private static ScimAuditResource ParseResource(PathString path)
    {
        var segments = path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (segments.Length < 3)
        {
            return new ScimAuditResource("unknown", null);
        }

        var resourceType = segments[2];
        var resourceId = segments.Length >= 4 ? segments[3] : null;

        if (segments.Length >= 5 && string.Equals(segments[4], "Certificates", StringComparison.OrdinalIgnoreCase))
        {
            resourceType = "ClientCertificates";
            resourceId = segments.Length >= 6 ? segments[5] : segments[3];
        }
        else if (segments.Length >= 5 && string.Equals(segments[3], "Certificates", StringComparison.OrdinalIgnoreCase))
        {
            resourceType = "ClientCertificates";
            resourceId = segments[4];
        }

        return new ScimAuditResource(resourceType, resourceId);
    }

    [LoggerMessage(
        EventId = 12001,
        Level = LogLevel.Information,
        Message = "ScimAdminMutation {Method} {ResourceType} {ResourceId} {CallerSubject} {CallerClientId} {CorrelationId} {StatusCode} {RemoteIpAddress}")]
    private static partial void LogScimAdminMutation(
        ILogger logger,
        string method,
        string resourceType,
        string? resourceId,
        string callerSubject,
        string callerClientId,
        string correlationId,
        int statusCode,
        string remoteIpAddress);

    private void LogAudit(HttpContext context)
    {
        var request = context.Request;
        var resource = ScimAdminAuditMiddleware.ParseResource(request.Path);

        ScimAdminAuditMiddleware.LogScimAdminMutation(
            this._logger,
            request.Method,
            resource.Type,
            resource.Id,
            context.User.FindFirst("sub")?.Value ?? "unknown",
            context.User.FindFirst("client_id")?.Value ?? "unknown",
            context.Response.Headers[_correlationIdHeader].ToString(),
            context.Response.StatusCode,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private sealed record ScimAuditResource(string Type, string? Id);
}
