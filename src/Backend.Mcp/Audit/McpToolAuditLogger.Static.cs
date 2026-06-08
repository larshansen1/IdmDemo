using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Backend.Mcp.Audit;

public sealed partial class McpToolAuditLogger
{
    private static string FormatScopes(IReadOnlyList<string> scopes)
    {
        var values = scopes as string[] ?? scopes.ToArray();
        return values.Length == 0 ? string.Empty : string.Join(' ', values);
    }

    private static string? TryReadCorrelationId(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return TryReadCorrelationId(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadCorrelationId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "correlationId", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = TryReadCorrelationId(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryReadCorrelationId(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    [LoggerMessage(EventId = 6100, EventName = ToolInvokedEventName, Level = LogLevel.Information, Message = "{AuditEvent} Tool={ToolName} Subject={Subject} ClientId={ClientId} Scopes={Scopes} Instance={Instance} Profile={Profile} Transport={Transport} ReadOnly={ReadOnly} Destructive={Destructive} RequiresCertificateScope={RequiresCertificateScope} Confirm={Confirm} ResourceId={ResourceId} CertificateId={CertificateId}")]
    private static partial void LogToolInvoked(
        ILogger logger,
        string auditEvent,
        string toolName,
        string? subject,
        string? clientId,
        string scopes,
        string instance,
        string profile,
        string transport,
        bool readOnly,
        bool destructive,
        bool requiresCertificateScope,
        bool? confirm,
        string? resourceId,
        string? certificateId);

    [LoggerMessage(EventId = 6101, EventName = ToolDeniedEventName, Level = LogLevel.Warning, Message = "{AuditEvent} Tool={ToolName} Subject={Subject} ClientId={ClientId} Scopes={Scopes} Instance={Instance} Profile={Profile} Transport={Transport} ReadOnly={ReadOnly} Destructive={Destructive} RequiresCertificateScope={RequiresCertificateScope} Confirm={Confirm} ResourceId={ResourceId} CertificateId={CertificateId} Reason={Reason}")]
    private static partial void LogToolDenied(
        ILogger logger,
        string auditEvent,
        string toolName,
        string? subject,
        string? clientId,
        string scopes,
        string instance,
        string profile,
        string transport,
        bool readOnly,
        bool destructive,
        bool requiresCertificateScope,
        bool? confirm,
        string? resourceId,
        string? certificateId,
        string reason);

    [LoggerMessage(EventId = 6102, EventName = ToolFailedEventName, Level = LogLevel.Warning, Message = "{AuditEvent} Tool={ToolName} Subject={Subject} ClientId={ClientId} Scopes={Scopes} Instance={Instance} Profile={Profile} Transport={Transport} ReadOnly={ReadOnly} Destructive={Destructive} RequiresCertificateScope={RequiresCertificateScope} Confirm={Confirm} ResourceId={ResourceId} CertificateId={CertificateId} CorrelationId={CorrelationId} Reason={Reason}")]
    private static partial void LogToolFailed(
        ILogger logger,
        Exception? exception,
        string auditEvent,
        string toolName,
        string? subject,
        string? clientId,
        string scopes,
        string instance,
        string profile,
        string transport,
        bool readOnly,
        bool destructive,
        bool requiresCertificateScope,
        bool? confirm,
        string? resourceId,
        string? certificateId,
        string? correlationId,
        string reason);

    [LoggerMessage(EventId = 6103, EventName = ToolSucceededEventName, Level = LogLevel.Information, Message = "{AuditEvent} Tool={ToolName} Subject={Subject} ClientId={ClientId} Scopes={Scopes} Instance={Instance} Profile={Profile} Transport={Transport} ReadOnly={ReadOnly} Destructive={Destructive} RequiresCertificateScope={RequiresCertificateScope} Confirm={Confirm} ResourceId={ResourceId} CertificateId={CertificateId} CorrelationId={CorrelationId}")]
    private static partial void LogToolSucceeded(
        ILogger logger,
        string auditEvent,
        string toolName,
        string? subject,
        string? clientId,
        string scopes,
        string instance,
        string profile,
        string transport,
        bool readOnly,
        bool destructive,
        bool requiresCertificateScope,
        bool? confirm,
        string? resourceId,
        string? certificateId,
        string? correlationId);

    [LoggerMessage(EventId = 6104, EventName = DestructiveToolSucceededEventName, Level = LogLevel.Warning, Message = "{AuditEvent} Tool={ToolName} Subject={Subject} ClientId={ClientId} Scopes={Scopes} Instance={Instance} Profile={Profile} Transport={Transport} ResourceId={ResourceId} CertificateId={CertificateId} CorrelationId={CorrelationId}")]
    private static partial void LogDestructiveToolSucceeded(
        ILogger logger,
        string auditEvent,
        string toolName,
        string? subject,
        string? clientId,
        string scopes,
        string instance,
        string profile,
        string transport,
        string? resourceId,
        string? certificateId,
        string? correlationId);
}
