using System.Security.Cryptography.X509Certificates;

namespace Backend.As.Domain;

public interface IIssuanceContextProvider
{
    Task<IssuanceContext> ResolveAsync(
        string? clientId,
        X509Certificate2? certificate,
        CancellationToken cancellationToken);
}
