using System.Security.Cryptography;

namespace Backend.Domain.Services;

public sealed class JwtSigningKey
{
    public string KeyId { get; init; } = string.Empty;

    public RSAParameters Parameters { get; init; }
}
