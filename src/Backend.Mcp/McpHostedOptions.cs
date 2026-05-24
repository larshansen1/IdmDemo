using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpHostedOptions
{
    public bool? RequireDpop { get; init; }

    public bool? AllowBearerTokensForDevelopment { get; init; }

    public bool AllowNonLocalDevelopmentBinding { get; init; }

    [Required]
    public string Audience { get; init; } = "idm-demo-mcp";
}
