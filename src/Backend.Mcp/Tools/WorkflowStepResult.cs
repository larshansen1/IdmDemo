namespace Backend.Mcp.Tools;

public sealed record WorkflowStepResult(
    string Name,
    string Status,
    string? CorrelationId,
    string? Detail);
