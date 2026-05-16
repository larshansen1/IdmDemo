namespace Backend.Mcp;

public interface IMcpMutationGuard
{
    void EnsureMutationAllowed();

    void EnsureDestructiveAllowed(bool confirm);
}
