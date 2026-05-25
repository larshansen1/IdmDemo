namespace Backend.Mcp.Audit;

public sealed record McpToolAuditContext(
    string ToolName,
    string? Subject,
    string? ClientId,
    IReadOnlyList<string> Scopes,
    string Instance,
    string? ResourceId,
    string? CertificateId,
    bool? Confirm,
    string Profile,
    string Transport,
    bool ReadOnly,
    bool Destructive,
    bool RequiresCertificateScope,
    string? CorrelationId = null);
