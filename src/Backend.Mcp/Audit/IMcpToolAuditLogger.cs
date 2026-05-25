using ModelContextProtocol.Protocol;

namespace Backend.Mcp.Audit;

public interface IMcpToolAuditLogger
{
    void ToolInvoked(McpToolAuditContext context);

    void ToolDenied(McpToolAuditContext context, string reason);

    void ToolFailed(McpToolAuditContext context, string reason, Exception? exception = null);

    void ToolSucceeded(McpToolAuditContext context, CallToolResult result);
}
