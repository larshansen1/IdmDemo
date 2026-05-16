using System.Net;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Mcp;
using Backend.Mcp.Api;
using Backend.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmAdminToolsTests
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
    public async Task OnboardMachineClientAsync_CertificateFailure_ReturnsPartialFailure()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        var client = new ClientResponse
        {
            Id = clientRecordId.ToString(),
            ClientId = "orders-service",
            Active = true,
            AssignedRoles = ["service"],
            AssignedScopes = ["orders.read"],
        };
        apiClient.ListClientsAsync(null, "clientId eq \"orders-service\"", Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<ClientResponse>>(
                "local",
                "list-correlation",
                new ScimListResponse<ClientResponse>()));
        apiClient.CreateClientAsync(null, Arg.Any<CreateClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ClientResponse>("local", "create-correlation", client));
        apiClient.CreateCertificateAsync(null, clientRecordId, Arg.Any<CreateCertificateRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<IdmApiCallResult<CertificateResponse>>>(_ =>
                throw new IdmApiException(HttpStatusCode.BadRequest, "cert-correlation", "CSR is invalid."));
        var tools = CreateTools(apiClient);

        var result = await tools.OnboardMachineClientAsync(
            "orders-service",
            assignedRoles: ["service"],
            assignedScopes: ["orders.read"],
            certificateMode: "csr",
            certificateSigningRequestPem: "bad-csr");

        Assert.Equal("partial_failure", result.Status);
        Assert.Equal("orders-service", result.Client!.ClientId);
        Assert.Null(result.Certificate);
        Assert.Contains(result.Steps, step => step.Name == "create_client" && step.Status == "succeeded");
        Assert.Contains(result.Steps, step => step.Name == "onboarding" && step.Status == "failed");
    }

    [Fact]
    public async Task OnboardMachineClientAsync_NoCertificate_ReturnsNextStep()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var client = new ClientResponse
        {
            Id = Guid.NewGuid().ToString(),
            ClientId = "orders-service",
            Active = true,
        };
        apiClient.ListClientsAsync(null, "clientId eq \"orders-service\"", Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<ClientResponse>>(
                "local",
                "list-correlation",
                new ScimListResponse<ClientResponse>()));
        apiClient.CreateClientAsync(null, Arg.Any<CreateClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ClientResponse>("local", "create-correlation", client));
        var tools = CreateTools(apiClient);

        var result = await tools.OnboardMachineClientAsync("orders-service");

        Assert.Equal("succeeded", result.Status);
        Assert.Contains("Register or issue a client certificate before requesting tokens.", result.NextSteps);
        await apiClient.DidNotReceiveWithAnyArgs()
            .CreateCertificateAsync(default, default, default!, default);
    }

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
                new CertificateResponse { ClientId = "order-agent" }));
        var tools = CreateTools(apiClient);

        await tools.IssueClientCertificateFromCsrAsync("order-agent", "csr-pem");

        await apiClient.Received(1)
            .CreateCertificateAsync(
                null,
                clientRecordId,
                Arg.Is<CreateCertificateRequest>(request =>
                    request.Mode == "csr" && request.CertificateSigningRequestPem == "csr-pem"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InspectClientCredentialStatusAsync_ExternalClientId_ResolvesRecordId()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var client = new ClientResponse
        {
            Id = clientRecordId.ToString(),
            ClientId = "order-agent",
            Active = true,
        };
        apiClient.ListClientsAsync(null, "clientId eq \"order-agent\"", Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<ClientResponse>>(
                "local",
                "list-client-correlation",
                new ScimListResponse<ClientResponse>
                {
                    TotalResults = 1,
                    ItemsPerPage = 1,
                    Resources = [client],
                }));
        apiClient.GetClientAsync(null, clientRecordId, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ClientResponse>("local", "get-client-correlation", client));
        apiClient.ListCertificatesAsync(null, clientRecordId, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<ScimListResponse<CertificateResponse>>(
                "local",
                "list-cert-correlation",
                new ScimListResponse<CertificateResponse>
                {
                    TotalResults = 1,
                    ItemsPerPage = 1,
                    Resources =
                    [
                        new CertificateResponse
                        {
                            Status = "Active",
                            ExpiresAt = expiresAt,
                        },
                    ],
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.InspectClientCredentialStatusAsync("order-agent");

        Assert.Equal(clientRecordId.ToString(), result.ClientId);
        Assert.Equal("order-agent", result.ExternalClientId);
        Assert.Equal(1, result.CertificateCount);
        Assert.Equal(1, result.ActiveCertificateCount);
        Assert.Equal(expiresAt, result.NextCertificateExpiry);
    }

    private static IdmAdminTools CreateTools(IIdmApiClient apiClient)
    {
        var guard = new McpMutationGuard(Options.Create(new McpRuntimeOptions()));
        return new IdmAdminTools(apiClient, guard, NullLogger<IdmAdminTools>.Instance);
    }
}
