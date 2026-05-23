using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpHostedOptions
{
    public bool RequireDpop { get; init; } = true;

    public bool AllowBearerTokensForDevelopment { get; init; }

    [Required]
    public string Audience { get; init; } = "idm-demo-mcp";
}
