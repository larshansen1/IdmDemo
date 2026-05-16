namespace Backend.Mcp;

public interface IIdmApiInstanceResolver
{
    ResolvedIdmApiInstance Resolve(string? instanceName);
}
