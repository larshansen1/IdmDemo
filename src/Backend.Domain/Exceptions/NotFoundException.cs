using System.Diagnostics.CodeAnalysis;

namespace Backend.Domain.Exceptions;

[ExcludeFromCodeCoverage]
public sealed class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NotFoundException(string resourceType, string identifier)
        : base($"{resourceType} '{identifier}' was not found.")
    {
        this.ResourceType = resourceType;
        this.Identifier = identifier;
    }

    public string? ResourceType { get; }

    public string? Identifier { get; }
}
