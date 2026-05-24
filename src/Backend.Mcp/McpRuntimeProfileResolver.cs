namespace Backend.Mcp;

public static class McpRuntimeProfileResolver
{
    public static McpEffectiveRuntimeSettings Resolve(McpRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var profile = ResolveProfile(options);
        var audience = options.Hosted.Audience;

        return profile switch
        {
            McpProfile.LocalStdio => new McpEffectiveRuntimeSettings(
                profile,
                McpTransport.Stdio,
                RequiresCallerAuthentication: false,
                RequireDpop: false,
                AllowBearerTokensForDevelopment: false,
                audience,
                options.ReadOnly ?? false),
            McpProfile.LocalHostedDevelopment => new McpEffectiveRuntimeSettings(
                profile,
                McpTransport.Http,
                RequiresCallerAuthentication: true,
                RequireDpop: false,
                AllowBearerTokensForDevelopment: true,
                audience,
                options.ReadOnly ?? false),
            McpProfile.HostedProduction => new McpEffectiveRuntimeSettings(
                profile,
                McpTransport.Http,
                RequiresCallerAuthentication: true,
                RequireDpop: true,
                AllowBearerTokensForDevelopment: false,
                audience,
                options.ReadOnly ?? true),
            _ => throw new InvalidOperationException($"Unsupported MCP profile '{profile}'."),
        };
    }

    private static McpProfile ResolveProfile(McpRuntimeOptions options)
    {
        if (options.Profile is { } profile)
        {
            return profile;
        }

        if (options.Transport != McpTransport.Http)
        {
            return McpProfile.LocalStdio;
        }

        return options.Hosted.AllowBearerTokensForDevelopment == true
            ? McpProfile.LocalHostedDevelopment
            : McpProfile.HostedProduction;
    }
}
