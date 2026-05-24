namespace Backend.Mcp;

public interface IMcpToolPolicyProvider
{
    McpToolPolicy GetPolicy(string toolName);
}
