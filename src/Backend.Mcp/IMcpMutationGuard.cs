using System.Text.Json;

namespace Backend.Mcp;

public interface IMcpMutationGuard
{
    void EnsureMutationAllowed();

    void EnsureDestructiveAllowed(bool confirm);

    void EnsureToolAllowed(string toolName, IDictionary<string, JsonElement>? arguments);
}
