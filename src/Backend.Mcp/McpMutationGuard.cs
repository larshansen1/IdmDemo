using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpMutationGuard : IMcpMutationGuard
{
    private readonly McpEffectiveRuntimeSettings _runtimeSettings;
    private readonly IMcpToolPolicyProvider _policyProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public McpMutationGuard(
        IOptions<McpRuntimeOptions> options,
        IMcpToolPolicyProvider policyProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policyProvider);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        this._runtimeSettings = McpRuntimeProfileResolver.Resolve(options.Value);
        this._policyProvider = policyProvider;
        this._httpContextAccessor = httpContextAccessor;
    }

    public void EnsureMutationAllowed()
    {
        if (this._runtimeSettings.ReadOnly)
        {
            throw new McpToolException("This MCP server is running in read-only mode.");
        }
    }

    public void EnsureDestructiveAllowed(bool confirm)
    {
        this.EnsureMutationAllowed();

        if (!confirm)
        {
            throw new McpToolException("This destructive tool requires confirm: true.");
        }
    }

    public void EnsureToolAllowed(string toolName, IDictionary<string, JsonElement>? arguments)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new McpToolException("MCP tool name is required.");
        }

        var policy = this._policyProvider.GetPolicy(toolName);

        if (this._runtimeSettings.Transport != McpTransport.Http)
        {
            this.EnsureLocalToolAllowed(policy, arguments);
            return;
        }

        this.EnsureHostedToolAllowed(policy, arguments);
    }

    private static bool ReadConfirm(IDictionary<string, JsonElement>? arguments)
    {
        return arguments is not null
            && arguments.TryGetValue("confirm", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static void RequireScope(ClaimsPrincipal user, string scope)
    {
        if (ReadScopes(user).Contains(scope, StringComparer.Ordinal))
        {
            return;
        }

        throw new McpToolException($"Hosted MCP caller requires scope '{scope}'.");
    }

    private static IEnumerable<string> ReadScopes(ClaimsPrincipal user)
    {
        return user.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private void EnsureLocalToolAllowed(McpToolPolicy policy, IDictionary<string, JsonElement>? arguments)
    {
        if (policy.Destructive)
        {
            this.EnsureDestructiveAllowed(ReadConfirm(arguments));
            return;
        }

        if (!policy.ReadOnly)
        {
            this.EnsureMutationAllowed();
        }
    }

    private void EnsureHostedToolAllowed(McpToolPolicy policy, IDictionary<string, JsonElement>? arguments)
    {
        var user = this._httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new McpToolException("Hosted MCP caller is not authenticated.");
        }

        if (policy.ReadOnly)
        {
            RequireScope(user, McpScopes.Read);
            return;
        }

        if (policy.Destructive)
        {
            this.EnsureDestructiveAllowed(ReadConfirm(arguments));
            RequireScope(user, McpScopes.Destructive);
        }
        else
        {
            this.EnsureMutationAllowed();
            RequireScope(user, McpScopes.Write);
        }

        if (policy.RequiresCertificateScope)
        {
            RequireScope(user, McpScopes.Certificates);
        }
    }
}
