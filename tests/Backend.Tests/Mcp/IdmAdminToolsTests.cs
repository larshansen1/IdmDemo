using System.Globalization;
using System.Net;
using Backend.Application.Models.Auth;
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

    [Fact]
    public async Task RotateMachineClientCertificateAsync_IssuesCertificateAndRevokesPreviousCertificate()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        var previousCertificateId = Guid.NewGuid();
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
                new ScimListResponse<CertificateResponse>
                {
                    TotalResults = 1,
                    ItemsPerPage = 1,
                    Resources =
                    [
                        new CertificateResponse
                        {
                            Id = previousCertificateId.ToString(),
                            Status = "Active",
                            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
                        },
                    ],
                }));
        apiClient.CreateCertificateAsync(null, clientRecordId, Arg.Any<CreateCertificateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<CertificateResponse>(
                "local",
                "issue-correlation",
                new CertificateResponse
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientId = "order-agent",
                    CertificatePem = "certificate-pem",
                    Status = "Active",
                }));
        apiClient.RevokeCertificateAsync(null, clientRecordId, previousCertificateId, Arg.Any<RevokeCertificateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<CertificateResponse>(
                "local",
                "revoke-correlation",
                new CertificateResponse
                {
                    Id = previousCertificateId.ToString(),
                    ClientId = "order-agent",
                    Status = "Revoked",
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.RotateMachineClientCertificateAsync(
            "order-agent",
            "csr-pem",
            revokeCertificateId: previousCertificateId,
            confirmRevoke: true,
            reason: "rotation");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.ExistingActiveCertificateCount);
        Assert.Equal("certificate-pem", result.IssuedCertificate!.CertificatePem);
        Assert.Equal("Revoked", result.RevokedCertificate!.Status);
        Assert.Contains(result.Steps, step => step.Name == "issue_certificate" && step.Status == "succeeded");
        Assert.Contains(result.Steps, step => step.Name == "revoke_previous_certificate" && step.Status == "succeeded");
    }

    [Fact]
    public async Task RotateMachineClientCertificateAsync_RevokeWithoutConfirmation_ThrowsBeforeApiCall()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var tools = CreateTools(apiClient);

        await Assert.ThrowsAsync<McpToolException>(() =>
            tools.RotateMachineClientCertificateAsync(
                "order-agent",
                "csr-pem",
                revokeCertificateId: Guid.NewGuid()));

        await apiClient.DidNotReceiveWithAnyArgs()
            .CreateCertificateAsync(default, default, default!, default);
    }

    [Fact]
    public async Task PrepareDpopClientCredentialInstructionsAsync_ReturnsDiscoveryBackedInstructions()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        apiClient.GetDiscoveryAsync(null, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<DiscoveryResponse>(
                "local",
                "discovery-correlation",
                new DiscoveryResponse
                {
                    Issuer = "https://issuer.example",
                    TokenEndpoint = new Uri("https://issuer.example/connect/token"),
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.PrepareDpopClientCredentialInstructionsAsync("order-agent", "idm-demo-mcp");

        Assert.Equal("order-agent", result.ClientId);
        Assert.Equal("idm-demo-mcp", result.McpAudience);
        Assert.Equal("https://issuer.example", result.AuthorizationServer.Issuer);
        Assert.Contains(result.Instructions, instruction => instruction.Contains("Authorization: DPoP", StringComparison.Ordinal));
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

    [Fact]
    public async Task PreflightMachineClientDeploymentAsync_ReturnsBlockingIssuesForMissingScopeAndInactiveClient()
    {
        var apiClient = Substitute.For<IIdmApiClient>();
        var clientRecordId = Guid.NewGuid();
        var client = new ClientResponse
        {
            Id = clientRecordId.ToString(),
            ClientId = "order-agent",
            Active = false,
            AssignedRoles = ["service"],
            AssignedScopes = ["orders.read"],
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
                    TotalResults = 0,
                    ItemsPerPage = 0,
                }));
        var tools = CreateTools(apiClient);

        var result = await tools.PreflightMachineClientDeploymentAsync(
            "order-agent",
            requiredRoles: ["service"],
            requiredScopes: ["orders.read", "orders.write"]);

        Assert.False(result.Ready);
        Assert.Contains("Machine client is inactive.", result.BlockingIssues);
        Assert.Contains("Machine client has no active certificate.", result.BlockingIssues);
        Assert.Contains("Machine client is missing required scope 'orders.write'.", result.BlockingIssues);
    }

    private static IdmAdminTools CreateTools(IIdmApiClient apiClient)
    {
        var guard = new McpMutationGuard(
            Options.Create(new McpRuntimeOptions()),
            new McpToolPolicyProvider(),
            new HttpContextAccessor());
        return new IdmAdminTools(apiClient, guard, NullLogger<IdmAdminTools>.Instance);
    }
}
