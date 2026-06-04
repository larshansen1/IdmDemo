using System.Text.Json;
using Backend.Mcp;
using Backend.Mcp.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpToolAuditTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Create_CapturesCallerPolicyAndDestructiveArguments()
    {
        var context = new DefaultHttpContext();
        context.Items[typeof(McpCallerContext)] = new McpCallerContext(
            "subject-1",
            "orders-agent",
            [McpScopes.Certificates, McpScopes.Destructive]);
        var factory = new McpToolAuditContextFactory(
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new McpRuntimeOptions
            {
                Profile = McpProfile.LocalHostedDevelopment,
                DefaultInstance = "local",
            }));
        var policy = new McpToolPolicy(
            "idm_revoke_client_certificate",
            ReadOnly: false,
            Destructive: true,
            RequiresCertificateScope: true);

        var auditContext = factory.Create(
            "idm_revoke_client_certificate",
            CreateArguments(
                ("clientId", "orders-agent"),
                ("certificateId", Guid.Parse("11111111-1111-1111-1111-111111111111")),
                ("confirm", true),
                ("instance", "remote")),
            policy);

        Assert.Equal("idm_revoke_client_certificate", auditContext.ToolName);
        Assert.Equal("subject-1", auditContext.Subject);
        Assert.Equal("orders-agent", auditContext.ClientId);
        Assert.Equal([McpScopes.Certificates, McpScopes.Destructive], auditContext.Scopes);
        Assert.Equal("remote", auditContext.Instance);
        Assert.Equal("orders-agent", auditContext.ResourceId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", auditContext.CertificateId);
        Assert.True(auditContext.Confirm);
        Assert.True(auditContext.Destructive);
        Assert.True(auditContext.RequiresCertificateScope);
        Assert.Equal(nameof(McpProfile.LocalHostedDevelopment), auditContext.Profile);
        Assert.Equal(nameof(McpTransport.Http), auditContext.Transport);
    }

    [Fact]
    public void ToolDenied_EmitsExpectedAuditEventAndSecurityContext()
    {
        var logger = new CapturingLogger<McpToolAuditLogger>();
        var auditLogger = new McpToolAuditLogger(logger);
        var context = new McpToolAuditContext(
            "idm_delete_user",
            "subject-1",
            "admin-agent",
            [McpScopes.Read],
            "local",
            "22222222-2222-2222-2222-222222222222",
            null,
            false,
            nameof(McpProfile.HostedProduction),
            nameof(McpTransport.Http),
            ReadOnly: false,
            Destructive: true,
            RequiresCertificateScope: false);

        auditLogger.ToolDenied(context, "This destructive tool requires confirm: true.");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(McpToolAuditLogger.ToolDeniedEventName, entry.EventId.Name);
        Assert.Contains(McpToolAuditLogger.ToolDeniedEventName, entry.Message, StringComparison.Ordinal);
        Assert.Contains("idm_delete_user", entry.Message, StringComparison.Ordinal);
        Assert.Contains("admin-agent", entry.Message, StringComparison.Ordinal);
        Assert.Contains("This destructive tool requires confirm: true.", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractCorrelationId_ReadsNestedJsonTextContent()
    {
        var result = new CallToolResult
        {
            IsError = false,
            Content =
            [
                new TextContentBlock { Text = "plain text" },
                new TextContentBlock { Text = """{"result":{"correlationId":"api-correlation"}}""" },
            ],
        };

        Assert.Equal("api-correlation", McpToolAuditLogger.ExtractCorrelationId(result));
    }

    private static Dictionary<string, JsonElement> CreateArguments(params (string Name, object Value)[] values)
    {
        var properties = values.Select(value => $"\"{value.Name}\":{Serialize(value.Value)}");
        using var document = JsonDocument.Parse("{" + string.Join(",", properties) + "}");
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, _jsonOptions);
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
