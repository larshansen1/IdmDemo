using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpRuntimeOptions
{
    public const string SectionName = "Mcp";

    [Required]
    public string DefaultInstance { get; init; } = "local";

    public bool ReadOnly { get; init; }
}
