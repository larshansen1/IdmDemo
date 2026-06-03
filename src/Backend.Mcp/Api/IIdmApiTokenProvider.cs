namespace Backend.Mcp.Api;

public interface IIdmApiTokenProvider
{
    Task<string> GetAccessTokenAsync(string instanceName, CancellationToken cancellationToken = default);
}
