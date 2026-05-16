namespace Backend.Mcp;

public sealed class McpConfigurationException : InvalidOperationException
{
    public McpConfigurationException()
    {
    }

    public McpConfigurationException(string message)
        : base(message)
    {
    }

    public McpConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
