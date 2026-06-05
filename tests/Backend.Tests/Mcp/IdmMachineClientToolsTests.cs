using System.Globalization;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Mcp;
using Backend.Mcp.Api;
using Backend.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmMachineClientToolsTests
{
    [Fact]
    public async Task DeleteMachineClientAsync_MissingConfirmation_ThrowsBeforeApiCall()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var tools = CreateTools(apiClient);

        await Assert.ThrowsAsync<McpToolException>(() =>
            tools.DeleteMachineClientAsync(Guid.NewGuid(), false));

        await apiClient.DidNotReceiveWithAnyArgs()
            .DeleteClientAsync(default, default, default);
    }

    [Fact]
    public async Task ListMachineClientsAsync_IncludesActiveCertificatesFromCertificateCollection()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        apiClient.ListClientsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<ClientResponse>>(
                "local",
                "list-client-correlation",
                new ScimListResponse<ClientResponse>
                {
                    TotalResults = 1,
                    ItemsPerPage = 1,
                    Resources =
                    [
                        new ClientResponse
                        {
                            Id = clientRecordId.ToString(),
                            ClientId = "sales-order-client",
                            Active = true,
                            AssignedRoles = ["service-user"],
                            AssignedScopes = ["sales.reader"],
                        },
                    ],
                }));
        apiClient.ListCertificatesAsync(null, clientRecordId, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<CertificateResponse>>(
                "local",
                "list-cert-correlation",
                new ScimListResponse<CertificateResponse>
                {
                    TotalResults = 2,
                    ItemsPerPage = 2,
                    Resources =
                    [
                        new CertificateResponse
                        {
                            Id = Guid.NewGuid().ToString(),
                            Status = "Revoked",
                            ThumbprintSha256 = "revoked-thumbprint",
                            ExpiresAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z", CultureInfo.InvariantCulture),
                        },
                        new CertificateResponse
                        {
                            Id = Guid.NewGuid().ToString(),
                            DisplayName = "order-agent",
                            Subject = "CN=order-agent",
                            Issuer = "CN=IdmDemo Local Development CA",
                            Status = "Active",
                            ThumbprintSha256 = "active-thumbprint",
                            ExpiresAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture),
                        },
                    ],
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.ListMachineClientsAsync();

        Assert.False(result.IsError);
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("\"activeCertificateCount\":1", StringComparison.Ordinal));
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("active-thumbprint", StringComparison.Ordinal));
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("Legacy single-certificate fields do not reflect the certificate collection.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListMachineClientsAsync_ClientIdFilter_ForwardsTrimmedFilter()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        apiClient.ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<ClientResponse>>(
                "local",
                "list-client-correlation",
                new ScimListResponse<ClientResponse>()));
        var tools = CreateTools(apiClient);

        var result = await tools.ListMachineClientsAsync("  clientId eq \"order-agent\"  ");

        Assert.False(result.IsError);
        await apiClient.Received(1)
            .ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMachineClientsAsync_UnsupportedFilterAttribute_ReturnsToolErrorBeforeApiCall()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var tools = CreateTools(apiClient);

        var result = await tools.ListMachineClientsAsync("active eq \"false\"");

        Assert.True(result.IsError);
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("active", StringComparison.Ordinal)
                && content.ToString()!.Contains("not supported", StringComparison.Ordinal));
        await apiClient.DidNotReceiveWithAnyArgs()
            .ListClientsAsync(default, default, default);
    }

    [Fact]
    public async Task ListMachineClientsAsync_MalformedFilter_ReturnsToolErrorBeforeApiCall()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var tools = CreateTools(apiClient);

        var result = await tools.ListMachineClientsAsync("clientId co \"order\"");

        Assert.True(result.IsError);
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("attributeName eq", StringComparison.Ordinal)
                && content.ToString()!.Contains("is supported", StringComparison.Ordinal));
        await apiClient.DidNotReceiveWithAnyArgs()
            .ListClientsAsync(default, default, default);
    }

    private static IdmMachineClientTools CreateTools(IIdmApiClient apiClient)
    {
        var guard = new McpMutationGuard(
            Options.Create(new McpRuntimeOptions()),
            new McpToolPolicyProvider(),
            new HttpContextAccessor());
        return new IdmMachineClientTools(apiClient, guard, NullLogger<IdmMachineClientTools>.Instance);
    }
}
