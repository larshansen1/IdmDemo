namespace Backend.Mcp;

public sealed record McpToolPolicy(
    string ToolName,
    bool ReadOnly,
    bool Destructive,
    bool RequiresCertificateScope);
