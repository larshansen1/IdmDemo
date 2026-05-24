using Backend.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpRuntimeOptionsTests
{
    [Fact]
    public void Defaults_ResolveLocalStdioProfile()
    {
        var options = new McpRuntimeOptions();
        var effective = McpRuntimeProfileResolver.Resolve(options);

        Assert.Null(options.Profile);
        Assert.Equal(McpProfile.LocalStdio, effective.Profile);
        Assert.Equal(McpTransport.Stdio, effective.Transport);
        Assert.False(effective.RequiresCallerAuthentication);
        Assert.False(effective.RequireDpop);
        Assert.False(effective.AllowBearerTokensForDevelopment);
        Assert.False(effective.ReadOnly);
        Assert.Equal("idm-demo-mcp", options.Hosted.Audience);
    }

    [Fact]
    public void Resolve_LocalHostedDevelopment_AllowsBearerAndDpop()
    {
        var options = new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment };

        var effective = McpRuntimeProfileResolver.Resolve(options);

        Assert.Equal(McpTransport.Http, effective.Transport);
        Assert.True(effective.RequiresCallerAuthentication);
        Assert.False(effective.RequireDpop);
        Assert.True(effective.AllowBearerTokensForDevelopment);
        Assert.False(effective.ReadOnly);
    }

    [Fact]
    public void Resolve_HostedProduction_RequiresDpopAndDefaultsReadOnly()
    {
        var options = new McpRuntimeOptions { Profile = McpProfile.HostedProduction };

        var effective = McpRuntimeProfileResolver.Resolve(options);

        Assert.Equal(McpTransport.Http, effective.Transport);
        Assert.True(effective.RequiresCallerAuthentication);
        Assert.True(effective.RequireDpop);
        Assert.False(effective.AllowBearerTokensForDevelopment);
        Assert.True(effective.ReadOnly);
    }

    [Fact]
    public void Resolve_LegacyHttpWithDevelopmentBearerInfersLocalHostedDevelopment()
    {
        var options = new McpRuntimeOptions
        {
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions { AllowBearerTokensForDevelopment = true },
        };

        var effective = McpRuntimeProfileResolver.Resolve(options);

        Assert.Equal(McpProfile.LocalHostedDevelopment, effective.Profile);
        Assert.Equal(McpTransport.Http, effective.Transport);
        Assert.True(effective.AllowBearerTokensForDevelopment);
        Assert.False(effective.RequireDpop);
    }

    [Fact]
    public void Resolve_LegacyHttpWithoutDevelopmentBearerInfersHostedProduction()
    {
        var options = new McpRuntimeOptions
        {
            Transport = McpTransport.Http,
        };

        var effective = McpRuntimeProfileResolver.Resolve(options);

        Assert.Equal(McpProfile.HostedProduction, effective.Profile);
        Assert.Equal(McpTransport.Http, effective.Transport);
        Assert.True(effective.RequireDpop);
        Assert.True(effective.ReadOnly);
    }

    [Fact]
    public void Validate_HostedProductionWithBearerEnabled_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.HostedProduction,
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions
            {
                AllowBearerTokensForDevelopment = true,
                Audience = "idm-demo-mcp",
            },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("AllowBearerTokensForDevelopment", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HostedProductionWithDpopDisabled_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.HostedProduction,
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions
            {
                RequireDpop = false,
                Audience = "idm-demo-mcp",
            },
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RequireDpop", StringComparison.Ordinal));
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
    public void Validate_ProfileTransportMismatch_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.HostedProduction,
            Transport = McpTransport.Stdio,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Transport", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LocalHostedDevelopmentWithPublicBindingOutsideDevelopment_Fails()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:5100",
            })
            .Build();
        var validator = new McpRuntimeOptionsValidator(configuration, new TestHostEnvironment("Production"));
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.LocalHostedDevelopment,
            Transport = McpTransport.Http,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("localhost binding", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LocalHostedDevelopmentWithLocalhostBindingOutsideDevelopment_Succeeds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://localhost:5100;http://127.0.0.1:5101",
            })
            .Build();
        var validator = new McpRuntimeOptionsValidator(configuration, new TestHostEnvironment("Production"));
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.LocalHostedDevelopment,
            Transport = McpTransport.Http,
        };

        var result = validator.Validate(null, options);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_LocalHostedDevelopmentWithExplicitPublicBindingOverride_Succeeds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:5100",
            })
            .Build();
        var validator = new McpRuntimeOptionsValidator(configuration, new TestHostEnvironment("Production"));
        var options = new McpRuntimeOptions
        {
            Profile = McpProfile.LocalHostedDevelopment,
            Transport = McpTransport.Http,
            Hosted = new McpHostedOptions { AllowNonLocalDevelopmentBinding = true },
        };

        var result = validator.Validate(null, options);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_InvalidProfile_Fails()
    {
        var validator = new McpRuntimeOptionsValidator();
        var options = new McpRuntimeOptions
        {
            Profile = (McpProfile)999,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Profile", StringComparison.Ordinal));
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            this.EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Backend.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
