using System.Security.Claims;
using Backend.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Backend.IntegrationTests.Middleware;

public sealed class ScimAdminAuditMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ScimMutation_LogsCallerCorrelationAndResult()
    {
        var logger = new CapturingLogger<ScimAdminAuditMiddleware>();
        var middleware = new ScimAdminAuditMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                return Task.CompletedTask;
            },
            logger);
        var context = CreateContext(HttpMethods.Post, "/scim/v2/Clients/11111111-1111-1111-1111-111111111111/Certificates");

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(12001, entry.EventId.Id);
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ClientCertificates", entry.Message, StringComparison.Ordinal);
        Assert.Contains("cert-admin", entry.Message, StringComparison.Ordinal);
        Assert.Contains("api-correlation", entry.Message, StringComparison.Ordinal);
        Assert.Contains("201", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_ScimRead_DoesNotLog()
    {
        var logger = new CapturingLogger<ScimAdminAuditMiddleware>();
        var middleware = new ScimAdminAuditMiddleware(_ => Task.CompletedTask, logger);
        var context = CreateContext(HttpMethods.Get, "/scim/v2/Clients");

        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Entries);
    }

    private static DefaultHttpContext CreateContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Headers["X-Correlation-Id"] = "api-correlation";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "subject-1"));
        identity.AddClaim(new Claim("client_id", "cert-admin"));
        context.User = new ClaimsPrincipal(identity);

        return context;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            this.Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
