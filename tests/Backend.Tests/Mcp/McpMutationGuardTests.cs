using System.Text.Json;
using Backend.Mcp;
using Backend.Mcp.RateLimit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpMutationGuardTests
{
    [Fact]
    public void EnsureMutationAllowed_ReadOnlyMode_ThrowsToolException()
    {
        var guard = CreateGuard(new McpRuntimeOptions { ReadOnly = true });

        Assert.Throws<McpToolException>(guard.EnsureMutationAllowed);
    }

    [Fact]
    public void EnsureDestructiveAllowed_MissingConfirmation_ThrowsToolException()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        Assert.Throws<McpToolException>(() => guard.EnsureDestructiveAllowed(false));
    }

    [Fact]
    public void EnsureDestructiveAllowed_ConfirmedMutation_DoesNotThrow()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        guard.EnsureDestructiveAllowed(true);
    }

    [Fact]
    public void EnsureToolAllowed_HostedReadToolWithReadScope_DoesNotThrow()
    {
        var guard = CreateHostedGuard(McpScopes.Read);

        guard.EnsureToolAllowed("idm_list_machine_clients", null);
    }

    [Fact]
    public void EnsureToolAllowed_HostedWriteToolMissingWriteScope_ThrowsToolException()
    {
        var guard = CreateHostedGuard(McpScopes.Read);

        Assert.Throws<McpToolException>(() => guard.EnsureToolAllowed("idm_create_user", null));
    }

    [Fact]
    public void EnsureToolAllowed_HostedDestructiveToolRequiresConfirmAndScope()
    {
        var guard = CreateHostedGuard(McpScopes.Destructive);

        Assert.Throws<McpToolException>(() => guard.EnsureToolAllowed("idm_delete_user", null));
    }

    [Fact]
    public void EnsureToolAllowed_HostedDestructiveToolWithConfirmAndScope_DoesNotThrow()
    {
        var guard = CreateHostedGuard(McpScopes.Destructive);

        guard.EnsureToolAllowed("idm_delete_user", CreateArguments(("confirm", true)));
    }

    [Fact]
    public void EnsureToolAllowed_HostedCertificateMutationRequiresCertificateScope()
    {
        var guard = CreateHostedGuard(McpScopes.Write);

        Assert.Throws<McpToolException>(() => guard.EnsureToolAllowed("idm_issue_client_certificate_from_csr", null));
    }

    [Fact]
    public void EnsureToolAllowed_HostedCertificateMutationWithRequiredScopes_DoesNotThrow()
    {
        var guard = CreateHostedGuard(McpScopes.Write, McpScopes.Certificates);

        guard.EnsureToolAllowed("idm_issue_client_certificate_from_csr", null);
    }

    [Fact]
    public void EnsureToolAllowed_HostedCertificateMutationAcceptsSpaceSeparatedScopeClaim()
    {
        var guard = CreateHostedGuard($"{McpScopes.Write} {McpScopes.Certificates}");

        guard.EnsureToolAllowed("idm_issue_client_certificate_from_csr", null);
    }

    [Fact]
    public void EnsureToolAllowed_HostedToolWithoutAuthenticatedCaller_ThrowsToolException()
    {
        var guard = new McpMutationGuard(
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            new McpToolPolicyProvider(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new DestructiveCallRateLimiter());

        Assert.Throws<McpToolException>(() => guard.EnsureToolAllowed("idm_list_machine_clients", null));
    }

    [Fact]
    public void EnsureToolAllowed_LocalDestructiveToolReadsConfirmArgument()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        guard.EnsureToolAllowed("idm_delete_user", CreateArguments(("confirm", true)));
    }

    [Fact]
    public void EnsureToolAllowed_LocalDestructiveToolWithFalseConfirm_ThrowsToolException()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        Assert.Throws<McpToolException>(() => guard.EnsureToolAllowed("idm_delete_user", CreateArguments(("confirm", false))));
    }

    [Fact]
    public void EnsureDestructiveAllowed_RateLimitExceeded_ThrowsToolException()
    {
        var rateLimiter = Substitute.For<IDestructiveCallRateLimiter>();
        rateLimiter.When(r => r.RecordAndEnforce(Arg.Any<string>()))
            .Throw(new McpToolException("Rate limit exceeded: at most 3 destructive operations per hour per caller."));
        var guard = CreateGuard(new McpRuntimeOptions(), rateLimiter);

        Assert.Throws<McpToolException>(() => guard.EnsureDestructiveAllowed(true));
    }

    [Fact]
    public void EnsureDestructiveAllowed_WithinRateLimit_DoesNotThrow()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        for (var i = 0; i < DestructiveCallRateLimiter.Limit; i++)
        {
            guard.EnsureDestructiveAllowed(true);
        }
    }

    [Fact]
    public void EnsureDestructiveAllowed_ExceedsRateLimit_ThrowsOnFourthCall()
    {
        var guard = CreateGuard(new McpRuntimeOptions());

        for (var i = 0; i < DestructiveCallRateLimiter.Limit; i++)
        {
            guard.EnsureDestructiveAllowed(true);
        }

        Assert.Throws<McpToolException>(() => guard.EnsureDestructiveAllowed(true));
    }

    private static McpMutationGuard CreateGuard(McpRuntimeOptions options, IDestructiveCallRateLimiter? rateLimiter = null)
    {
        return new McpMutationGuard(
            Options.Create(options),
            new McpToolPolicyProvider(),
            new HttpContextAccessor(),
            rateLimiter ?? new DestructiveCallRateLimiter());
    }

    private static Dictionary<string, JsonElement> CreateArguments(params (string Name, bool Value)[] values)
    {
        using var document = JsonDocument.Parse(
            "{" + string.Join(",", values.Select(value => $"\"{value.Name}\":{(value.Value ? "true" : "false")}")) + "}");
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static McpMutationGuard CreateHostedGuard(params string[] scopes)
    {
        var context = new DefaultHttpContext();
        var allScopes = scopes
            .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        context.Items[typeof(McpCallerContext)] = new McpCallerContext("subject", "client", allScopes);

        return new McpMutationGuard(
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            new McpToolPolicyProvider(),
            new HttpContextAccessor { HttpContext = context },
            new DestructiveCallRateLimiter());
    }
}
