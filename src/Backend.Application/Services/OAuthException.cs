namespace Backend.Application.Services;

public sealed class OAuthException : Exception
{
    public OAuthException()
    {
        this.Error = "invalid_request";
        this.Description = "The request is invalid.";
    }

    public OAuthException(string message)
        : base(message)
    {
        this.Error = "invalid_request";
        this.Description = message;
    }

    public OAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Error = "invalid_request";
        this.Description = message;
    }

    public OAuthException(string error, string description, int statusCode)
        : base(description)
    {
        this.Error = error;
        this.Description = description;
        this.StatusCode = statusCode;
    }

    public string Error { get; }

    public string Description { get; }

    public int StatusCode { get; }
}
