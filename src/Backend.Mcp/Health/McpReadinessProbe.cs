using Microsoft.Extensions.Options;

namespace Backend.Mcp.Health;

public sealed class McpReadinessProbe : IMcpReadinessProbe
{
    private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IdmApiInstancesOptions _instances;
    private readonly McpRuntimeOptions _runtimeOptions;
    private readonly McpEffectiveRuntimeSettings _runtimeSettings;

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
        this._runtimeSettings = McpRuntimeProfileResolver.Resolve(this._runtimeOptions);
    }

    public async Task<McpReadinessReport> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<string>();
        var errors = new List<string>();

        this.ValidateHostedAuthConfiguration(checks, errors);
        await this.CheckConfiguredInstancesAsync(checks, errors, cancellationToken).ConfigureAwait(false);

        return new McpReadinessReport(
            errors.Count == 0 ? "Healthy" : "Unhealthy",
            this._runtimeSettings.Profile.ToString(),
            this._runtimeSettings.Transport.ToString(),
            this._runtimeSettings.RequiresCallerAuthentication,
            this._runtimeSettings.RequireDpop,
            this._runtimeSettings.AllowBearerTokensForDevelopment,
            this._runtimeSettings.Audience,
            this._runtimeSettings.ReadOnly,
            this.CreateRawSettings(),
            this.CreateEffectiveSettings(),
            checks,
            errors);
    }

    private McpRawReadinessSettings CreateRawSettings()
    {
        return new McpRawReadinessSettings(
            this._runtimeOptions.Profile?.ToString(),
            this._runtimeOptions.Transport?.ToString(),
            this._runtimeOptions.Hosted.RequireDpop,
            this._runtimeOptions.Hosted.AllowBearerTokensForDevelopment,
            this._runtimeOptions.Hosted.Audience,
            this._runtimeOptions.ReadOnly);
    }

    private McpEffectiveReadinessSettings CreateEffectiveSettings()
    {
        return new McpEffectiveReadinessSettings(
            this._runtimeSettings.Profile.ToString(),
            this._runtimeSettings.Transport.ToString(),
            this._runtimeSettings.RequiresCallerAuthentication,
            this._runtimeSettings.RequireDpop,
            this._runtimeSettings.AllowBearerTokensForDevelopment,
            this._runtimeSettings.Audience,
            this._runtimeSettings.ReadOnly);
    }

    private void ValidateHostedAuthConfiguration(List<string> checks, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(this._runtimeSettings.Audience))
        {
            errors.Add("Mcp:Hosted:Audience is required.");
        }
        else
        {
            checks.Add($"MCP audience is configured for '{this._runtimeSettings.Audience}'.");
        }

        checks.Add($"MCP profile '{this._runtimeSettings.Profile}' resolves transport '{this._runtimeSettings.Transport}'.");

        if (!this._runtimeSettings.RequiresCallerAuthentication)
        {
            checks.Add("Hosted MCP caller authentication is not required for the selected profile.");
        }
        else if (this._runtimeSettings.RequireDpop)
        {
            checks.Add("Hosted MCP is configured to require DPoP-bound access tokens.");
        }
        else if (this._runtimeSettings.AllowBearerTokensForDevelopment)
        {
            checks.Add("Hosted MCP bearer tokens are enabled for development.");
        }
        else
        {
            errors.Add("Hosted MCP must require DPoP or explicitly allow bearer tokens for development.");
        }

        if (this._runtimeSettings.ReadOnly)
        {
            checks.Add("MCP read-only mode is enabled.");
        }
        else
        {
            checks.Add("MCP mutating tools are enabled subject to tool policy.");
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

            if (string.IsNullOrWhiteSpace(instance.ClientId))
            {
                errors.Add($"IdM API instance '{name}' is missing ClientId.");
                continue;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(instance.BaseUrl, ".well-known/openid-configuration"));

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
