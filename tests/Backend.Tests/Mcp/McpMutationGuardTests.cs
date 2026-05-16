using Backend.Mcp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpMutationGuardTests
{
    [Fact]
    public void EnsureMutationAllowed_ReadOnlyMode_ThrowsToolException()
    {
        var guard = new McpMutationGuard(Options.Create(new McpRuntimeOptions { ReadOnly = true }));

        Assert.Throws<McpToolException>(guard.EnsureMutationAllowed);
    }

    [Fact]
    public void EnsureDestructiveAllowed_MissingConfirmation_ThrowsToolException()
    {
        var guard = new McpMutationGuard(Options.Create(new McpRuntimeOptions()));

        Assert.Throws<McpToolException>(() => guard.EnsureDestructiveAllowed(false));
    }

    [Fact]
    public void EnsureDestructiveAllowed_ConfirmedMutation_DoesNotThrow()
    {
        var guard = new McpMutationGuard(Options.Create(new McpRuntimeOptions()));

        guard.EnsureDestructiveAllowed(true);
    }
}
