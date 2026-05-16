using System.Net;

namespace Backend.Mcp.Api;

public sealed class IdmApiException : InvalidOperationException
{
    public IdmApiException()
    {
    }

    public IdmApiException(string message)
        : base(message)
    {
    }

    public IdmApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IdmApiException(
        HttpStatusCode statusCode,
        string correlationId,
        string message)
        : base(message)
    {
        this.StatusCode = statusCode;
        this.CorrelationId = correlationId;
    }

    public IdmApiException(
        HttpStatusCode statusCode,
        string correlationId,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        this.StatusCode = statusCode;
        this.CorrelationId = correlationId;
    }

    public HttpStatusCode StatusCode { get; }

    public string CorrelationId { get; } = string.Empty;
}
