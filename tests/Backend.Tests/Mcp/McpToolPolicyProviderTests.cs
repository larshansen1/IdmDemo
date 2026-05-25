using Backend.Mcp;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpToolPolicyProviderTests
{
    [Fact]
    public void GetPolicy_ReadOnlyTool_ReturnsReadOnlyPolicy()
    {
        var provider = new McpToolPolicyProvider();

        var policy = provider.GetPolicy("idm_list_machine_clients");

        Assert.Equal("idm_list_machine_clients", policy.ToolName);
        Assert.True(policy.ReadOnly);
        Assert.False(policy.Destructive);
        Assert.False(policy.RequiresCertificateScope);
    }

    [Fact]
    public void GetPolicy_DestructiveTool_ReturnsDestructivePolicy()
    {
        var provider = new McpToolPolicyProvider();

        var policy = provider.GetPolicy("idm_delete_user");

        Assert.False(policy.ReadOnly);
        Assert.True(policy.Destructive);
        Assert.False(policy.RequiresCertificateScope);
    }

    [Fact]
    public void GetPolicy_CertificateTool_ReturnsCertificateScopePolicy()
    {
        var provider = new McpToolPolicyProvider();

        var policy = provider.GetPolicy("idm_issue_client_certificate_from_csr");

        Assert.False(policy.ReadOnly);
        Assert.False(policy.Destructive);
        Assert.True(policy.RequiresCertificateScope);
    }

    [Fact]
    public void GetPolicy_CertificateWorkflowTool_ReturnsCertificateScopePolicy()
    {
        var provider = new McpToolPolicyProvider();

        var policy = provider.GetPolicy("idm_rotate_machine_client_certificate");

        Assert.False(policy.ReadOnly);
        Assert.False(policy.Destructive);
        Assert.True(policy.RequiresCertificateScope);
    }

    [Fact]
    public void GetPolicy_UnregisteredTool_ThrowsToolException()
    {
        var provider = new McpToolPolicyProvider();

        Assert.Throws<McpToolException>(() => provider.GetPolicy("idm_missing_tool"));
    }
}
