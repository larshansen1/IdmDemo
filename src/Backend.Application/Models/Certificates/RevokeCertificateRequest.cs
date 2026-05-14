namespace Backend.Application.Models.Certificates;

public sealed class RevokeCertificateRequest
{
    public string? Reason { get; init; }
}
