using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Backend.Mcp.Audit;

public sealed class McpToolAuditContextFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly McpRuntimeOptions _runtimeOptions;
    private readonly McpEffectiveRuntimeSettings _runtimeSettings;

    public McpToolAuditContextFactory(
        IHttpContextAccessor httpContextAccessor,
        IOptions<McpRuntimeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(options);

        this._httpContextAccessor = httpContextAccessor;
        this._runtimeOptions = options.Value;
        this._runtimeSettings = McpRuntimeProfileResolver.Resolve(options.Value);
    }

    public McpToolAuditContext Create(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        McpToolPolicy? policy)
    {
        var caller = this._httpContextAccessor.HttpContext?.Items[typeof(McpCallerContext)] as McpCallerContext;
        return new McpToolAuditContext(
            toolName,
            caller?.Subject,
            caller?.ClientId,
            (IReadOnlyList<string>?)caller?.Scopes ?? [],
            ReadInstance(arguments) ?? this._runtimeOptions.DefaultInstance,
            ReadString(arguments, "id") ?? ReadString(arguments, "clientId"),
            ReadString(arguments, "certificateId"),
            ReadConfirm(arguments),
            this._runtimeSettings.Profile.ToString(),
            this._runtimeSettings.Transport.ToString(),
            policy?.ReadOnly ?? false,
            policy?.Destructive ?? false,
            policy?.RequiresCertificateScope ?? false);
    }

    private static bool? ReadConfirm(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || !arguments.TryGetValue("confirm", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? ReadInstance(IDictionary<string, JsonElement>? arguments)
    {
        return ReadString(arguments, "instance");
    }

    private static string? ReadString(IDictionary<string, JsonElement>? arguments, string name)
    {
        if (arguments is null || !arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null,
        };
    }
}
