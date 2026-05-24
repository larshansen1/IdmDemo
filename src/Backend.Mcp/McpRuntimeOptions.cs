using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpRuntimeOptions
{
    public const string SectionName = "Mcp";

    public McpProfile? Profile { get; init; }

    public McpTransport? Transport { get; init; }

    [Required]
    public string DefaultInstance { get; init; } = "local";

    public bool? ReadOnly { get; init; }

    [Required]
    public McpHostedOptions Hosted { get; init; } = new();
}
