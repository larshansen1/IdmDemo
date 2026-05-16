using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpMutationGuard : IMcpMutationGuard
{
    private readonly McpRuntimeOptions _options;

    public McpMutationGuard(IOptions<McpRuntimeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this._options = options.Value;
    }

    public void EnsureMutationAllowed()
    {
        if (this._options.ReadOnly)
        {
            throw new McpToolException("This MCP server is running in read-only mode.");
        }
    }

    public void EnsureDestructiveAllowed(bool confirm)
    {
        this.EnsureMutationAllowed();

        if (!confirm)
        {
            throw new McpToolException("This destructive tool requires confirm: true.");
        }
    }
}
