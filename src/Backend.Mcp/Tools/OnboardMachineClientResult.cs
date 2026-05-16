using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;

namespace Backend.Mcp.Tools;

public sealed record OnboardMachineClientResult(
    string Instance,
    string Status,
    ClientResponse? Client,
    CertificateResponse? Certificate,
    IReadOnlyList<string> AssignedRoles,
    IReadOnlyList<string> AssignedScopes,
    IReadOnlyList<OnboardMachineClientStep> Steps,
    IReadOnlyList<string> NextSteps);
