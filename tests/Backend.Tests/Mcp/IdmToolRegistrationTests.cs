using Backend.Mcp.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmToolRegistrationTests
{
    [Fact]
    public void IdmAdminTools_ExposesExpectedToolNames()
    {
        var toolNames = typeof(IdmAdminTools)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                .OfType<McpServerToolAttribute>()
                .SingleOrDefault()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(toolNames.IsSupersetOf([
            "idm_create_user",
            "idm_get_user",
            "idm_update_user",
            "idm_delete_user",
            "idm_create_machine_client",
            "idm_get_machine_client",
            "idm_list_machine_clients",
            "idm_update_machine_client",
            "idm_delete_machine_client",
            "idm_create_global_role",
            "idm_update_global_role",
            "idm_delete_global_role",
            "idm_create_global_scope",
            "idm_update_global_scope",
            "idm_delete_global_scope",
            "idm_register_external_client_certificate",
            "idm_issue_client_certificate_from_csr",
            "idm_list_client_certificates",
            "idm_get_client_certificate",
            "idm_revoke_client_certificate",
            "idm_get_certificate_authority",
            "idm_get_authorization_server_metadata",
            "idm_get_jwks",
            "idm_inspect_client_credential_status",
            "idm_rotate_machine_client_certificate",
            "idm_prepare_dpop_client_credential_instructions",
            "idm_preflight_machine_client_deployment",
            "idm_onboard_machine_client",
        ]));
    }
}
