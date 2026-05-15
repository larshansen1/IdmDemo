namespace Backend.Application.Models.Auth;

public sealed class ValidatedDpopProof
{
    public string JwkThumbprint { get; init; } = string.Empty;
}
