using Backend.Application.Models.Certificates;

namespace Backend.Mcp.Tools;

public sealed record RotateMachineClientCertificateResult(
    string Instance,
    string Status,
    string ClientId,
    Guid ClientRecordId,
    int ExistingActiveCertificateCount,
    CertificateResponse? IssuedCertificate,
    CertificateResponse? RevokedCertificate,
    IReadOnlyList<WorkflowStepResult> Steps,
    IReadOnlyList<string> NextSteps);
