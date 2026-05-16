namespace Backend.Mcp.Tools;

public sealed record ClientCredentialStatusResult(
    string Instance,
    string CorrelationId,
    string ClientId,
    string ExternalClientId,
    bool Active,
    int CertificateCount,
    int ActiveCertificateCount,
    DateTimeOffset? NextCertificateExpiry);
