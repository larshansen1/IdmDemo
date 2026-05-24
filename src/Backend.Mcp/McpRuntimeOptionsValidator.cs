using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpRuntimeOptionsValidator : IValidateOptions<McpRuntimeOptions>
{
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;

    public McpRuntimeOptionsValidator()
    {
    }

    public McpRuntimeOptionsValidator(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        this._configuration = configuration;
        this._environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, McpRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Profile is not null && !Enum.IsDefined(options.Profile.Value))
        {
            failures.Add("Mcp:Profile must be LocalStdio, LocalHostedDevelopment, or HostedProduction.");
        }

        if (options.Transport is not null && !Enum.IsDefined(options.Transport.Value))
        {
            failures.Add("Mcp:Transport must be either Stdio or Http.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultInstance))
        {
            failures.Add("Mcp:DefaultInstance is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Hosted.Audience))
        {
            failures.Add("Mcp:Hosted:Audience is required.");
        }

        if (failures.Count == 0)
        {
            this.ValidateProfile(options, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsLocalOnlyUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(uri.Host, "::1", StringComparison.Ordinal)
            || string.Equals(uri.Host, "[::1]", StringComparison.Ordinal);
    }

    private static void ValidateTransportOverride(
        McpRuntimeOptions options,
        McpEffectiveRuntimeSettings effective,
        List<string> failures)
    {
        if (options.Transport is { } transport && transport != effective.Transport)
        {
            failures.Add($"Mcp:Transport must match the selected MCP profile '{effective.Profile}'.");
        }
    }

    private static void ValidateDpopOverride(
        McpRuntimeOptions options,
        McpEffectiveRuntimeSettings effective,
        List<string> failures)
    {
        if (options.Hosted.RequireDpop is { } requireDpop && requireDpop != effective.RequireDpop)
        {
            failures.Add($"Mcp:Hosted:RequireDpop contradicts the selected MCP profile '{effective.Profile}'.");
        }
    }

    private static void ValidateDevelopmentBearerOverride(
        McpRuntimeOptions options,
        McpEffectiveRuntimeSettings effective,
        List<string> failures)
    {
        if (options.Hosted.AllowBearerTokensForDevelopment is { } allowBearerTokens
            && allowBearerTokens != effective.AllowBearerTokensForDevelopment)
        {
            failures.Add(
                $"Mcp:Hosted:AllowBearerTokensForDevelopment contradicts the selected MCP profile '{effective.Profile}'.");
        }
    }

    private static void ValidateHostedAuthentication(McpEffectiveRuntimeSettings effective, List<string> failures)
    {
        if (effective.Transport == McpTransport.Http
            && !effective.RequireDpop
            && !effective.AllowBearerTokensForDevelopment)
        {
            failures.Add("Hosted MCP must require DPoP or explicitly allow bearer tokens for development.");
        }
    }

    private void ValidateProfile(McpRuntimeOptions options, List<string> failures)
    {
        var effective = McpRuntimeProfileResolver.Resolve(options);

        ValidateTransportOverride(options, effective, failures);
        ValidateDpopOverride(options, effective, failures);
        ValidateDevelopmentBearerOverride(options, effective, failures);
        ValidateHostedAuthentication(effective, failures);
        this.ValidateLocalHostedDevelopmentBinding(options, effective, failures);
    }

    private void ValidateLocalHostedDevelopmentBinding(
        McpRuntimeOptions options,
        McpEffectiveRuntimeSettings effective,
        List<string> failures)
    {
        if (effective.Profile == McpProfile.LocalHostedDevelopment
            && !this.IsDevelopmentOrTest()
            && !options.Hosted.AllowNonLocalDevelopmentBinding
            && !this.IsLocalOnlyHostedBinding())
        {
            failures.Add(
                "LocalHostedDevelopment requires localhost binding, development/test environment, or Mcp:Hosted:AllowNonLocalDevelopmentBinding=true.");
        }
    }

    private bool IsDevelopmentOrTest()
    {
        var environmentName = this._environment?.EnvironmentName
            ?? this._configuration?["ASPNETCORE_ENVIRONMENT"]
            ?? this._configuration?["DOTNET_ENVIRONMENT"];

        return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLocalOnlyHostedBinding()
    {
        var urls = this._configuration?["urls"]
            ?? this._configuration?["ASPNETCORE_URLS"]
            ?? this._configuration?["DOTNET_URLS"];

        if (string.IsNullOrWhiteSpace(urls))
        {
            return true;
        }

        return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(IsLocalOnlyUrl);
    }
}
