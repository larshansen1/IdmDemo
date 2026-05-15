namespace Backend.Application.Models.Auth;

public sealed class ValidatedAccessToken
{
    public string Subject { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public string? DpopJwkThumbprint { get; init; }

    public string? CertificateThumbprintSha256 { get; init; }
}
