using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class IdmApiInstanceResolver : IIdmApiInstanceResolver
{
    private readonly IdmApiInstancesOptions _instances;
    private readonly McpRuntimeOptions _runtimeOptions;

    public IdmApiInstanceResolver(
        IOptions<IdmApiInstancesOptions> instances,
        IOptions<McpRuntimeOptions> runtimeOptions)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(runtimeOptions);

        this._instances = instances.Value;
        this._runtimeOptions = runtimeOptions.Value;
    }

    public ResolvedIdmApiInstance Resolve(string? instanceName)
    {
        var name = string.IsNullOrWhiteSpace(instanceName)
            ? this._runtimeOptions.DefaultInstance
            : instanceName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpConfigurationException("No IdM API instance was selected.");
        }

        if (!this._instances.TryGetValue(name, out var instance))
        {
            throw new McpConfigurationException($"IdM API instance '{name}' is not configured.");
        }

        if (instance.BaseUrl is null)
        {
            throw new McpConfigurationException($"IdM API instance '{name}' is missing BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(instance.ClientId))
        {
            throw new McpConfigurationException($"IdM API instance '{name}' is missing ClientId.");
        }

        if (string.IsNullOrWhiteSpace(instance.ClientCertificatePath))
        {
            throw new McpConfigurationException($"IdM API instance '{name}' is missing ClientCertificatePath.");
        }

        return new ResolvedIdmApiInstance(name, instance.BaseUrl, instance.ClientId, instance.ClientCertificatePath, instance.AuthorityUrl);
    }
}
