using System.Text.Json;
using Backend.Mcp.RateLimit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Backend.Mcp;

public sealed class McpMutationGuard : IMcpMutationGuard
{
    private readonly McpEffectiveRuntimeSettings _runtimeSettings;
    private readonly IMcpToolPolicyProvider _policyProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDestructiveCallRateLimiter _rateLimiter;

    public McpMutationGuard(
        IOptions<McpRuntimeOptions> options,
        IMcpToolPolicyProvider policyProvider,
        IHttpContextAccessor httpContextAccessor,
        IDestructiveCallRateLimiter rateLimiter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policyProvider);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(rateLimiter);

        this._runtimeSettings = McpRuntimeProfileResolver.Resolve(options.Value);
        this._policyProvider = policyProvider;
        this._httpContextAccessor = httpContextAccessor;
        this._rateLimiter = rateLimiter;
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

        this._rateLimiter.RecordAndEnforce(this.GetCallerKey());
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

    private static void RequireScope(McpCallerContext caller, string scope)
    {
        if (!caller.HasScope(scope))
        {
            throw new McpToolException($"Hosted MCP caller requires scope '{scope}'.");
        }
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

    private string GetCallerKey()
    {
        var caller = this._httpContextAccessor.HttpContext?.Items[typeof(McpCallerContext)] as McpCallerContext;
        return caller?.ClientId ?? "local";
    }

    private void EnsureHostedToolAllowed(McpToolPolicy policy, IDictionary<string, JsonElement>? arguments)
    {
        var caller = this._httpContextAccessor.HttpContext?.Items[typeof(McpCallerContext)] as McpCallerContext;
        if (caller is null)
        {
            throw new McpToolException("Hosted MCP caller is not authenticated.");
        }

        if (policy.ReadOnly)
        {
            RequireScope(caller, McpScopes.Read);
            return;
        }

        if (policy.Destructive)
        {
            this.EnsureDestructiveAllowed(ReadConfirm(arguments));
            RequireScope(caller, McpScopes.Destructive);
        }
        else
        {
            this.EnsureMutationAllowed();
            RequireScope(caller, McpScopes.Write);
        }

        if (policy.RequiresCertificateScope)
        {
            RequireScope(caller, McpScopes.Certificates);
        }
    }
}
