namespace Backend.Domain.Services;

public interface IJwtSigningKeyStore
{
    Task<JwtSigningKey> GetActiveKeyAsync(CancellationToken cancellationToken = default);
}
