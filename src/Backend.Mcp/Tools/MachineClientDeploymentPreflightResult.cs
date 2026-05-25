using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;

namespace Backend.Mcp.Tools;

public sealed record MachineClientDeploymentPreflightResult(
    string Instance,
    string CorrelationId,
    string ClientId,
    Guid ClientRecordId,
    bool Ready,
    ClientResponse Client,
    int CertificateCount,
    int ActiveCertificateCount,
    DateTimeOffset? NextCertificateExpiry,
    IReadOnlyList<CertificateResponse> ActiveCertificates,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> SuggestedNextActions);
