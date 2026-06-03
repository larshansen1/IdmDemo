using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class IdmApiInstanceOptions
{
    [Required]
    public Uri? BaseUrl { get; init; }

    [Required]
    public string? ClientId { get; init; }

    [Required]
    public string? ClientCertificatePath { get; init; }
}
