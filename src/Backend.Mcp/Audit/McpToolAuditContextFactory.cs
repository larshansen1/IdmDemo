using System.Security.Claims;
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
        var user = this._httpContextAccessor.HttpContext?.User;
        return new McpToolAuditContext(
            toolName,
            ReadClaim(user, "sub", ClaimTypes.NameIdentifier),
            ReadClaim(user, "client_id"),
            ReadScopes(user),
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

    private static string? ReadClaim(ClaimsPrincipal? user, params string[] claimTypes)
    {
        if (user is null)
        {
            return null;
        }

        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string[] ReadScopes(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return [];
        }

        return user.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
