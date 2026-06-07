using System.Diagnostics.CodeAnalysis;

namespace Backend.Idp.Domain.Exceptions;

[ExcludeFromCodeCoverage]
public sealed class ValidationException : Exception
{
    public ValidationException()
        : base("A validation error occurred.")
    {
    }

    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
