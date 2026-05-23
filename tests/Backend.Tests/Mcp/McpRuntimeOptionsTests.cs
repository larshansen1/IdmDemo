using Backend.Mcp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpRuntimeOptionsTests
{
    [Fact]
    public void Defaults_UseStdioTransport()
    {
        var options = new McpRuntimeOptions();

        Assert.Equal(McpTransport.Stdio, options.Transport);
        Assert.True(options.Hosted.RequireDpop);
        Assert.False(options.Hosted.AllowBearerTokensForDevelopment);
        Assert.Equal("idm-demo-mcp", options.Hosted.Audience);
    }

    [Fact]
    public void Validate_HttpWithoutDpopOrDevelopmentBearer_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions
            {
                RequireDpop = false,
                AllowBearerTokensForDevelopment = false,
                Audience = "idm-demo-mcp",
            },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("require DPoP", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HttpWithDpopAndAudience_Succeeds()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Transport = McpTransport.Http,
            DefaultInstance = "local",
            Hosted = new McpHostedOptions
            {
                RequireDpop = true,
                Audience = "idm-demo-mcp",
            },
        };

        var result = validator.Validate(null, options);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_InvalidTransport_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Transport = (McpTransport)999,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Transport", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingDefaultInstance_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            DefaultInstance = string.Empty,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("DefaultInstance", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingHostedAudience_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Hosted = new McpHostedOptions
            {
                Audience = " ",
            },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Audience", StringComparison.Ordinal));
    }
}
