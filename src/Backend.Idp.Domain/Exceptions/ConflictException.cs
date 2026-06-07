using System.Diagnostics.CodeAnalysis;

namespace Backend.Idp.Domain.Exceptions;

[ExcludeFromCodeCoverage]
public sealed class ConflictException : Exception
{
    public ConflictException()
        : base("A conflicting resource already exists.")
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConflictException(string attributeName, string value)
        : base($"A resource with {attributeName} '{value}' already exists.")
    {
        this.AttributeName = attributeName;
        this.Value = value;
    }

    public string? AttributeName { get; }

    public string? Value { get; }
}
