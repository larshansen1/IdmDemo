using Microsoft.Extensions.Options;

namespace Backend.Mcp.Health;

public sealed class McpReadinessProbe : IMcpReadinessProbe
{
    private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IdmApiInstancesOptions _instances;
    private readonly McpRuntimeOptions _runtimeOptions;

    public McpReadinessProbe(
        IHttpClientFactory httpClientFactory,
        IOptions<IdmApiInstancesOptions> instances,
        IOptions<McpRuntimeOptions> runtimeOptions)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(runtimeOptions);

        this._httpClientFactory = httpClientFactory;
        this._instances = instances.Value;
        this._runtimeOptions = runtimeOptions.Value;
    }

    public async Task<McpReadinessReport> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<string>();
        var errors = new List<string>();

        this.ValidateHostedAuthConfiguration(checks, errors);
        await this.CheckConfiguredInstancesAsync(checks, errors, cancellationToken).ConfigureAwait(false);

        return new McpReadinessReport(
            errors.Count == 0 ? "Healthy" : "Unhealthy",
            this._runtimeOptions.Transport.ToString(),
            checks,
            errors);
    }

    private void ValidateHostedAuthConfiguration(List<string> checks, List<string> errors)
    {
        var hosted = this._runtimeOptions.Hosted;
        if (string.IsNullOrWhiteSpace(hosted.Audience))
        {
            errors.Add("Mcp:Hosted:Audience is required.");
        }
        else
        {
            checks.Add("Hosted MCP audience is configured.");
        }

        if (!hosted.RequireDpop && !hosted.AllowBearerTokensForDevelopment)
        {
            errors.Add("Hosted MCP must require DPoP or explicitly allow bearer tokens for development.");
        }
        else if (hosted.RequireDpop)
        {
            checks.Add("Hosted MCP is configured to require DPoP-bound access tokens.");
        }
        else
        {
            checks.Add("Hosted MCP bearer tokens are enabled for development.");
        }
    }

    private async Task CheckConfiguredInstancesAsync(
        List<string> checks,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (this._instances.Count == 0)
        {
            errors.Add("At least one IdmApiInstances entry is required.");
            return;
        }

        var client = this._httpClientFactory.CreateClient("idm-mcp-readiness");
        client.Timeout = _httpTimeout;

        foreach (var (name, instance) in this._instances)
        {
            if (instance.BaseUrl is null)
            {
                errors.Add($"IdM API instance '{name}' is missing BaseUrl.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(instance.ApiKey))
            {
                errors.Add($"IdM API instance '{name}' is missing ApiKey.");
                continue;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(instance.BaseUrl, ".well-known/openid-configuration"));
            request.Headers.Add("X-Api-Key", instance.ApiKey);

            try
            {
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    checks.Add($"IdM API instance '{name}' is reachable.");
                }
                else
                {
                    errors.Add($"IdM API instance '{name}' returned {(int)response.StatusCode} {response.StatusCode}.");
                }
            }
            catch (HttpRequestException exception)
            {
                errors.Add($"IdM API instance '{name}' is unreachable: {exception.Message}");
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"IdM API instance '{name}' readiness check timed out: {exception.Message}");
            }
        }
    }
}
