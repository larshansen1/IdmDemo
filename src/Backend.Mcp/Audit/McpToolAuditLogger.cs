using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Backend.Mcp.Audit;

[ExcludeFromCodeCoverage]
public sealed partial class McpToolAuditLogger(ILogger<McpToolAuditLogger> logger) : IMcpToolAuditLogger
{
    public const string ToolInvokedEventName = "McpToolInvoked";
    public const string ToolDeniedEventName = "McpToolDenied";
    public const string ToolFailedEventName = "McpToolFailed";
    public const string ToolSucceededEventName = "McpToolSucceeded";

    public static string? ExtractCorrelationId(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var content in result.Content)
        {
            if (content is not TextContentBlock textContent || string.IsNullOrWhiteSpace(textContent.Text))
            {
                continue;
            }

            var correlationId = TryReadCorrelationId(textContent.Text);
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId;
            }
        }

        return null;
    }

    public void ToolInvoked(McpToolAuditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        LogToolInvoked(
            logger,
            ToolInvokedEventName,
            context.ToolName,
            context.Subject,
            context.ClientId,
            FormatScopes(context.Scopes),
            context.Instance,
            context.Profile,
            context.Transport,
            context.ReadOnly,
            context.Destructive,
            context.RequiresCertificateScope,
            context.Confirm,
            context.ResourceId,
            context.CertificateId);
    }

    public void ToolDenied(McpToolAuditContext context, string reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        LogToolDenied(
            logger,
            ToolDeniedEventName,
            context.ToolName,
            context.Subject,
            context.ClientId,
            FormatScopes(context.Scopes),
            context.Instance,
            context.Profile,
            context.Transport,
            context.ReadOnly,
            context.Destructive,
            context.RequiresCertificateScope,
            context.Confirm,
            context.ResourceId,
            context.CertificateId,
            reason);
    }

    public void ToolFailed(McpToolAuditContext context, string reason, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        LogToolFailed(
            logger,
            exception,
            ToolFailedEventName,
            context.ToolName,
            context.Subject,
            context.ClientId,
            FormatScopes(context.Scopes),
            context.Instance,
            context.Profile,
            context.Transport,
            context.ReadOnly,
            context.Destructive,
            context.RequiresCertificateScope,
            context.Confirm,
            context.ResourceId,
            context.CertificateId,
            context.CorrelationId,
            reason);
    }

    public void ToolSucceeded(McpToolAuditContext context, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        LogToolSucceeded(
            logger,
            ToolSucceededEventName,
            context.ToolName,
            context.Subject,
            context.ClientId,
            FormatScopes(context.Scopes),
            context.Instance,
            context.Profile,
            context.Transport,
            context.ReadOnly,
            context.Destructive,
            context.RequiresCertificateScope,
            context.Confirm,
            context.ResourceId,
            context.CertificateId,
            ExtractCorrelationId(result) ?? context.CorrelationId);
    }
}
