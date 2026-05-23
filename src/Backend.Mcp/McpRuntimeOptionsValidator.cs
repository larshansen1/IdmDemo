using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpRuntimeOptionsValidator : IValidateOptions<McpRuntimeOptions>
{
    public ValidateOptionsResult Validate(string? name, McpRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!Enum.IsDefined(options.Transport))
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

        if (options.Transport == McpTransport.Http
            && !options.Hosted.RequireDpop
            && !options.Hosted.AllowBearerTokensForDevelopment)
        {
            failures.Add("Hosted MCP must require DPoP or explicitly allow bearer tokens for development.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
