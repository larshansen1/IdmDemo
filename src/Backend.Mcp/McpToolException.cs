namespace Backend.Mcp;

public sealed class McpToolException : InvalidOperationException
{
    public McpToolException()
    {
    }

    public McpToolException(string message)
        : base(message)
    {
    }

    public McpToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
