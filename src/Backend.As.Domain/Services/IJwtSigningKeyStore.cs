namespace Backend.As.Domain.Services;

public interface IJwtSigningKeyStore
{
    Task<JwtSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default);
}
