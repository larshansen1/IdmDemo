namespace Backend.Mcp;

public sealed record McpCallerContext(
    string Subject,
    string ClientId,
    IReadOnlyList<string> Scopes)
{
    public bool HasScope(string scope) =>
        this.Scopes.Contains(scope, StringComparer.Ordinal);
}
