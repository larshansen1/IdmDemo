namespace Backend.Mcp.Api;

public interface IIdmApiTokenProvider
{
    Task<BoundToken> GetBoundTokenAsync(string instanceName, CancellationToken cancellationToken = default);
}
