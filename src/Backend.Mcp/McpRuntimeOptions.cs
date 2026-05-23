using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpRuntimeOptions
{
    public const string SectionName = "Mcp";

    public McpTransport Transport { get; init; } = McpTransport.Stdio;

    [Required]
    public string DefaultInstance { get; init; } = "local";

    public bool ReadOnly { get; init; }

    [Required]
    public McpHostedOptions Hosted { get; init; } = new();
}
