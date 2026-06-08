using System.Net;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Mcp;
using Backend.Mcp.Api;
using Backend.Mcp.RateLimit;
using Backend.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmCertificateToolsTests
{
    [Fact]
    public async Task ListClientCertificatesAsync_ExternalClientId_ResolvesRecordId()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        apiClient.ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>())
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
                            ClientId = "order-agent",
                            Active = true,
                        },
                    ],
                }));
        apiClient.ListCertificatesAsync(null, clientRecordId, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<CertificateResponse>>(
                "local",
                "list-cert-correlation",
                new ScimListResponse<CertificateResponse>()));
        var tools = CreateTools(apiClient);

        await tools.ListClientCertificatesAsync("order-agent");

        await apiClient.Received(1)
            .ListCertificatesAsync(null, clientRecordId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueClientCertificateFromCsrAsync_ExternalClientId_ResolvesRecordId()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        apiClient.ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>())
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
                            ClientId = "order-agent",
                            Active = true,
                        },
                    ],
                }));
        apiClient.CreateCertificateAsync(null, clientRecordId, Arg.Any<CreateCertificateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<CertificateResponse>(
                "local",
                "create-cert-correlation",
                new CertificateResponse
                {
                    ClientId = "order-agent",
                    CertificatePem = "-----BEGIN CERTIFICATE-----\ntest-certificate\n-----END CERTIFICATE-----",
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.IssueClientCertificateFromCsrAsync("order-agent", "csr-pem");

        Assert.False(result.IsError);
        Assert.Equal("-----BEGIN CERTIFICATE-----\ntest-certificate\n-----END CERTIFICATE-----", result.Content[0].ToString());
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("create-cert-correlation", StringComparison.Ordinal));
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("\"certificatePem\":\"-----BEGIN CERTIFICATE-----", StringComparison.Ordinal));
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("Return certificatePem to the caller.", StringComparison.Ordinal));
        await apiClient.Received(1)
            .CreateCertificateAsync(
                null,
                clientRecordId,
                Arg.Is<CreateCertificateRequest>(request =>
                    request.Mode == "csr" && request.CertificateSigningRequestPem == "csr-pem"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueClientCertificateFromCsrAsync_ApiValidationFailure_ReturnsToolError()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        apiClient.ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>())
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
                            ClientId = "order-agent",
                            Active = true,
                        },
                    ],
                }));
        apiClient.CreateCertificateAsync(null, clientRecordId, Arg.Any<CreateCertificateRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<IdmApiCallResult<CertificateResponse>>>(_ =>
                throw new IdmApiException(
                    HttpStatusCode.BadRequest,
                    "cert-correlation",
                    "400 BadRequest: validityDays must be between 1 and 90. CorrelationId=cert-correlation"));
        var tools = CreateTools(apiClient);

        var result = await tools.IssueClientCertificateFromCsrAsync("order-agent", "csr-pem", validityDays: 365);

        Assert.True(result.IsError);
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("validityDays must be between 1 and 90", StringComparison.Ordinal));
        Assert.Contains(
            result.Content,
            content => content.ToString()!.Contains("cert-correlation", StringComparison.Ordinal));
    }

    private static IdmCertificateTools CreateTools(IIdmApiClient apiClient)
    {
        var guard = new McpMutationGuard(
            Options.Create(new McpRuntimeOptions()),
            new McpToolPolicyProvider(),
            new HttpContextAccessor(),
            new DestructiveCallRateLimiter());
        return new IdmCertificateTools(apiClient, guard);
    }
}
