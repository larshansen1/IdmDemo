using System.Reflection;
using Backend.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Backend.Mcp;

public sealed class McpToolPolicyProvider : IMcpToolPolicyProvider
{
    private static readonly HashSet<string> _certificateTools = new(StringComparer.Ordinal)
    {
        "idm_register_external_client_certificate",
        "idm_issue_client_certificate_from_csr",
        "idm_revoke_client_certificate",
        "idm_onboard_machine_client",
        "idm_rotate_machine_client_certificate",
    };

    private readonly Dictionary<string, McpToolPolicy> _policies;

    public McpToolPolicyProvider()
    {
        this._policies = new[]
            {
                typeof(IdmUserTools),
                typeof(IdmMachineClientTools),
                typeof(IdmCatalogTools),
                typeof(IdmCertificateTools),
                typeof(IdmWorkflowTools),
            }
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute =>
            {
                var toolName = attribute!.Name ?? string.Empty;
                var readOnly = attribute.ReadOnly is true;
                return new McpToolPolicy(
                    toolName,
                    readOnly,
                    !readOnly && attribute.Destructive is true,
                    _certificateTools.Contains(toolName));
            })
            .ToDictionary(policy => policy.ToolName, StringComparer.Ordinal);
    }

    public McpToolPolicy GetPolicy(string toolName)
    {
        if (this._policies.TryGetValue(toolName, out var policy))
        {
            return policy;
        }

        throw new McpToolException($"MCP tool '{toolName}' is not registered.");
    }
}
