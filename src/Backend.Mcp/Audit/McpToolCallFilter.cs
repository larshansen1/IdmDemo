using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backend.Mcp.Audit;

public sealed class McpToolCallFilter
{
    private readonly McpToolAuditContextFactory _auditContextFactory;
    private readonly IMcpToolAuditLogger _auditLogger;
    private readonly IMcpMutationGuard _guard;
    private readonly IMcpToolPolicyProvider _policyProvider;

    public McpToolCallFilter(
        McpToolAuditContextFactory auditContextFactory,
        IMcpToolAuditLogger auditLogger,
        IMcpMutationGuard guard,
        IMcpToolPolicyProvider policyProvider)
    {
        ArgumentNullException.ThrowIfNull(auditContextFactory);
        ArgumentNullException.ThrowIfNull(auditLogger);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(policyProvider);

        this._auditContextFactory = auditContextFactory;
        this._auditLogger = auditLogger;
        this._guard = guard;
        this._policyProvider = policyProvider;
    }

    public async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);

        var toolName = context.Params?.Name ?? string.Empty;
        var arguments = context.Params?.Arguments;
        var auditContext = this.CreateAuditContext(toolName, arguments);

        this._auditLogger.ToolInvoked(auditContext);

        try
        {
            return await this.InvokeAllowedToolAsync(next, context, toolName, arguments, auditContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (McpToolException exception)
        {
            this._auditLogger.ToolDenied(auditContext, exception.Message);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this._auditLogger.ToolFailed(auditContext, exception.Message, exception);
            throw;
        }
    }

    private McpToolAuditContext CreateAuditContext(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        return this._auditContextFactory.Create(toolName, arguments, this.TryGetPolicy(toolName));
    }

    private async ValueTask<CallToolResult> InvokeAllowedToolAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        McpToolAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        this._guard.EnsureToolAllowed(toolName, arguments);

        var result = await next(context, cancellationToken).ConfigureAwait(false);
        this.LogToolResult(auditContext, result);
        return result;
    }

    private void LogToolResult(McpToolAuditContext auditContext, CallToolResult result)
    {
        var resultContext = auditContext with { CorrelationId = McpToolAuditLogger.ExtractCorrelationId(result) };

        if (result.IsError == true)
        {
            this._auditLogger.ToolFailed(resultContext, "Tool returned an MCP error result.");
            return;
        }

        this._auditLogger.ToolSucceeded(resultContext, result);
    }

    private McpToolPolicy? TryGetPolicy(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        try
        {
            return this._policyProvider.GetPolicy(toolName);
        }
        catch (McpToolException)
        {
            return null;
        }
    }
}
