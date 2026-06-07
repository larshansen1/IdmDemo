using System.ComponentModel.DataAnnotations;

namespace Backend.Mcp;

public sealed class IdmApiInstanceOptions
{
    [Required]
    public Uri? BaseUrl { get; init; }

    /// <summary>
    /// Canonical issuer URL used as the DPoP <c>htu</c> base for token requests.
    /// Required when <see cref="BaseUrl"/> is an internal address that differs from the
    /// public issuer URL (e.g. <c>http://backend-api:8080</c> vs <c>https://auth.example.com</c>).
    /// Defaults to <see cref="BaseUrl"/> when not set.
    /// </summary>
    public Uri? AuthorityUrl { get; init; }

    [Required]
    public string? ClientId { get; init; }

    [Required]
    public string? ClientCertificatePath { get; init; }
}
