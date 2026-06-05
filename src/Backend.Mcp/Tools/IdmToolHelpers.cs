using System.Text.Json;
using Backend.Application.Models.Certificates;
using Backend.Mcp.Api;
using ModelContextProtocol.Protocol;

namespace Backend.Mcp.Tools;

internal static class IdmToolHelpers
{
    internal const string Succeeded = "succeeded";
    internal const string Failed = "failed";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] _issuedCertificateNextSteps =
    [
        "Return certificatePem to the caller.",
        "Use certificatePem with the private key that generated the CSR.",
    ];

    internal static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpToolException($"{parameterName} is required.");
        }
    }

    internal static string EscapeScimFilterValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    internal static CallToolResult CreateToolResult<T>(T value, bool isError)
    {
        return new CallToolResult
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(value, JsonOptions),
                },
            ],
        };
    }

    internal static CallToolResult CreateApiErrorToolResult(IdmApiException exception)
    {
        return CreateToolResult(
            new
            {
                error = exception.Message,
                statusCode = (int)exception.StatusCode,
                exception.CorrelationId,
            },
            true);
    }

    internal static CallToolResult CreateIssuedCertificateToolResult(IdmApiCallResult<CertificateResponse> result)
    {
        var metadata = new
        {
            result.Instance,
            result.CorrelationId,
            certificatePem = result.Value.CertificatePem,
            certificate = result.Value,
            nextSteps = _issuedCertificateNextSteps,
        };

        return new CallToolResult
        {
            IsError = false,
            Content =
            [
                new TextContentBlock
                {
                    Text = result.Value.CertificatePem,
                },
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(metadata, JsonOptions),
                },
            ],
        };
    }

    internal static async Task<Guid> ResolveClientRecordIdAsync(
        IIdmApiClient apiClient,
        string? instance,
        string clientId,
        CancellationToken cancellationToken)
    {
        RequireText(clientId, nameof(clientId));

        if (Guid.TryParse(clientId, out var clientRecordId))
        {
            return clientRecordId;
        }

        var filter = $"clientId eq \"{EscapeScimFilterValue(clientId)}\"";
        var found = await apiClient.ListClientsAsync(instance, filter, cancellationToken).ConfigureAwait(false);
        var matches = found.Value.Resources
            .Where(client => string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            0 => throw new McpToolException($"Machine client '{clientId}' was not found."),
            1 when Guid.TryParse(matches[0].Id, out var id) => id,
            1 => throw new McpToolException($"Machine client '{clientId}' has an invalid record id '{matches[0].Id}'."),
            _ => throw new McpToolException($"Machine client '{clientId}' matched multiple records."),
        };
    }
}
