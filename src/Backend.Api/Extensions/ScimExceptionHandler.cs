using Backend.Application.Models.Scim;
using Backend.Idp.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Extensions;

public sealed partial class ScimExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ScimExceptionHandler> _logger;

    public ScimExceptionHandler(ILogger<ScimExceptionHandler> logger)
    {
        this._logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var (status, scimType, detail) = MapException(exception);

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(this._logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/json";

        var error = new ScimError
        {
            Status = status,
            ScimType = scimType,
            Detail = detail,
        };

        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken).ConfigureAwait(false);

        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception processing {Method} {Path}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        Exception ex,
        string method,
        string path);

    private static (int Status, string? ScimType, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException vex => (StatusCodes.Status400BadRequest, "invalidValue", vex.Message),
            ConflictException cex => (StatusCodes.Status409Conflict, "uniqueness", cex.Message),
            NotFoundException nex => (StatusCodes.Status404NotFound, null, nex.Message),
            _ => (StatusCodes.Status500InternalServerError, null, "An unexpected error occurred."),
        };
    }
}
