using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class McpHostedOptions
{
    public bool? RequireDpop { get; init; }

    public bool? AllowBearerTokensForDevelopment { get; init; }

    public bool AllowNonLocalDevelopmentBinding { get; init; }

    [Required]
    public string Audience { get; init; } = "idm-demo-mcp";

    /// <summary>
    /// Trusted reverse-proxy addresses in CIDR notation ("10.0.0.0/8") or as single IPs ("10.0.0.1").
    /// When empty, only loopback addresses are trusted (ASP.NET Core default).
    /// </summary>
    public IReadOnlyList<string> TrustedProxyNetworks { get; init; } = [];
}
