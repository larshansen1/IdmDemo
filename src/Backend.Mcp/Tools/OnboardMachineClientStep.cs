namespace Backend.Mcp.Tools;

public sealed record OnboardMachineClientStep(
    string Name,
    string Status,
    string? CorrelationId,
    string? Detail);
