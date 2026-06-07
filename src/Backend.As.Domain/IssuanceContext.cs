using System.Security.Cryptography.X509Certificates;

namespace Backend.As.Domain;

public sealed record IssuanceContext(
    Guid ClientRecordId,
    string ClientId,
    X509Certificate2 Certificate,
    IReadOnlyList<string> ActiveScopes,
    IReadOnlyList<string> ActiveRoles);
