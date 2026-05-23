namespace Backend.Mcp.Health;

public interface IMcpReadinessProbe
{
    Task<McpReadinessReport> CheckAsync(CancellationToken cancellationToken);
}
