using Backend.Application.Models.Auth;

namespace Backend.Mcp.Tools;

public sealed record DpopClientCredentialInstructionsResult(
    string Instance,
    string CorrelationId,
    string ClientId,
    string McpAudience,
    DiscoveryResponse AuthorizationServer,
    IReadOnlyList<WorkflowStepResult> Steps,
    IReadOnlyList<string> Instructions);
