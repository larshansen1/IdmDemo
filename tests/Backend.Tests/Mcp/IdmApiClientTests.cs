using System.Net;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Models.Users;
using Backend.Mcp;
using Backend.Mcp.Api;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class IdmApiClientTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<Func<IdmApiClient, Guid, Guid, Task>, object, HttpMethod, string> ApiCalls()
    {
        return new TheoryData<Func<IdmApiClient, Guid, Guid, Task>, object, HttpMethod, string>
        {
            {
                (client, _, _) => client.CreateUserAsync(null, new CreateUserRequest { UserName = "alice" }, CancellationToken.None),
                new UserResponse { Id = Guid.NewGuid().ToString(), UserName = "alice" },
                HttpMethod.Post,
                "scim/v2/Users"
            },
            {
                (client, id, _) => client.GetUserAsync(null, id, CancellationToken.None),
                new UserResponse { Id = Guid.NewGuid().ToString(), UserName = "alice" },
                HttpMethod.Get,
                "scim/v2/Users/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, id, _) => client.UpdateUserAsync(null, id, new UpdateUserRequest { UserName = "alice" }, CancellationToken.None),
                new UserResponse { Id = Guid.NewGuid().ToString(), UserName = "alice" },
                HttpMethod.Put,
                "scim/v2/Users/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, _, _) => client.CreateRoleAsync(null, new CreateRoleRequest { Value = "service-admin" }, CancellationToken.None),
                new RoleResponse { Id = Guid.NewGuid().ToString(), Value = "service-admin" },
                HttpMethod.Post,
                "scim/v2/Roles"
            },
            {
                (client, id, _) => client.UpdateRoleAsync(null, id, new UpdateRoleRequest { Value = "service-admin" }, CancellationToken.None),
                new RoleResponse { Id = Guid.NewGuid().ToString(), Value = "service-admin" },
                HttpMethod.Put,
                "scim/v2/Roles/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, id, _) => client.DeleteRoleAsync(null, id, CancellationToken.None),
                new EmptyResponse(),
                HttpMethod.Delete,
                "scim/v2/Roles/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, _, _) => client.CreateScopeAsync(null, new CreateScopeRequest { Value = "orders.read" }, CancellationToken.None),
                new ScopeResponse { Id = Guid.NewGuid().ToString(), Value = "orders.read" },
                HttpMethod.Post,
                "scim/v2/Scopes"
            },
            {
                (client, id, _) => client.UpdateScopeAsync(null, id, new UpdateScopeRequest { Value = "orders.read" }, CancellationToken.None),
                new ScopeResponse { Id = Guid.NewGuid().ToString(), Value = "orders.read" },
                HttpMethod.Put,
                "scim/v2/Scopes/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, id, _) => client.DeleteScopeAsync(null, id, CancellationToken.None),
                new EmptyResponse(),
                HttpMethod.Delete,
                "scim/v2/Scopes/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, id, _) => client.UpdateClientAsync(null, id, new UpdateClientRequest { ClientId = "orders-service" }, CancellationToken.None),
                new ClientResponse { Id = Guid.NewGuid().ToString(), ClientId = "orders-service" },
                HttpMethod.Put,
                "scim/v2/Clients/11111111-1111-1111-1111-111111111111"
            },
            {
                (client, id, _) => client.CreateCertificateAsync(null, id, new CreateCertificateRequest { Mode = "csr" }, CancellationToken.None),
                new CertificateResponse { Id = Guid.NewGuid().ToString(), ClientId = "orders-service" },
                HttpMethod.Post,
                "scim/v2/Clients/11111111-1111-1111-1111-111111111111/Certificates"
            },
            {
                (client, id, _) => client.ListCertificatesAsync(null, id, CancellationToken.None),
                new ScimListResponse<CertificateResponse>(),
                HttpMethod.Get,
                "scim/v2/Clients/11111111-1111-1111-1111-111111111111/Certificates"
            },
            {
                (client, id, certificateId) => client.GetCertificateAsync(null, id, certificateId, CancellationToken.None),
                new CertificateResponse { Id = Guid.NewGuid().ToString(), ClientId = "orders-service" },
                HttpMethod.Get,
                "scim/v2/Clients/11111111-1111-1111-1111-111111111111/Certificates/22222222-2222-2222-2222-222222222222"
            },
            {
                (client, id, certificateId) => client.RevokeCertificateAsync(null, id, certificateId, new RevokeCertificateRequest(), CancellationToken.None),
                new CertificateResponse { Id = Guid.NewGuid().ToString(), ClientId = "orders-service" },
                HttpMethod.Post,
                "scim/v2/Clients/11111111-1111-1111-1111-111111111111/Certificates/22222222-2222-2222-2222-222222222222/Revoke"
            },
            {
                (client, _, _) => client.GetCertificateAuthorityAsync(null, CancellationToken.None),
                new CertificateAuthorityResponse { CertificatePem = "pem" },
                HttpMethod.Get,
                "scim/v2/Certificates/Authority"
            },
            {
                (client, _, _) => client.GetDiscoveryAsync(null, CancellationToken.None),
                new DiscoveryResponse { Issuer = "https://issuer.example" },
                HttpMethod.Get,
                ".well-known/openid-configuration"
            },
            {
                (client, _, _) => client.GetJwksAsync(null, CancellationToken.None),
                new JwksResponse(),
                HttpMethod.Get,
                ".well-known/jwks.json"
            },
        };
    }

    [Fact]
    public async Task GetClientAsync_ExplicitInstance_SendsBearerTokenAndCorrelationId()
    {
        var clientId = Guid.NewGuid();
        using var handler = new CapturingHandler(new ClientResponse
        {
            Id = clientId.ToString(),
            ClientId = "orders-service",
            Active = true,
        });
        var apiClient = CreateClient(handler);

        var result = await apiClient.GetClientAsync("test", clientId, CancellationToken.None);

        Assert.Equal("test", result.Instance);
        Assert.Equal("orders-service", result.Value.ClientId);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(new Uri($"https://localhost:5003/scim/v2/Clients/{clientId:D}"), handler.Request.RequestUri);
        Assert.True(handler.Request.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("Bearer test-bearer-token", handler.Request.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task GetClientAsync_ErrorResponse_ThrowsApiExceptionWithoutScimDetail()
    {
        using var handler = new CapturingHandler(
            HttpStatusCode.NotFound,
            new { detail = "Client was not found.", status = 404 });
        var apiClient = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<IdmApiException>(() =>
            apiClient.GetClientAsync(null, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("404 NotFound", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Client was not found.", exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(exception.CorrelationId));
    }

    [Fact]
    public async Task GetClientAsync_NonScimErrorResponse_ThrowsApiExceptionWithoutBody()
    {
        const string internalError = "stack trace: /srv/idm-demo/internal.sql";
        using var handler = new TextResponseHandler(HttpStatusCode.InternalServerError, internalError);
        var apiClient = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<IdmApiException>(() =>
            apiClient.GetClientAsync(null, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Contains("500 InternalServerError", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(internalError, exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(exception.CorrelationId));
    }

    [Theory]
    [MemberData(nameof(ApiCalls))]
    public async Task ApiMethods_SendExpectedRequest(
        Func<IdmApiClient, Guid, Guid, Task> act,
        object responseBody,
        HttpMethod expectedMethod,
        string expectedPath)
    {
        ArgumentNullException.ThrowIfNull(act);

        var clientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var certificateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var handler = new CapturingHandler(responseBody);
        var apiClient = CreateClient(handler);

        await act(apiClient, clientId, certificateId);

        Assert.Equal(expectedMethod, handler.Request!.Method);
        Assert.Equal(new Uri($"https://localhost:5001/{expectedPath}"), handler.Request.RequestUri);
    }

    [Fact]
    public async Task DeleteClientAsync_NoContent_ReturnsEmptyResponse()
    {
        var clientId = Guid.NewGuid();
        using var handler = new CapturingHandler(HttpStatusCode.NoContent, null);
        var apiClient = CreateClient(handler);

        var result = await apiClient.DeleteClientAsync(null, clientId, CancellationToken.None);

        Assert.IsType<EmptyResponse>(result.Value);
        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Equal(new Uri($"https://localhost:5001/scim/v2/Clients/{clientId:D}"), handler.Request.RequestUri);
    }

    [Fact]
    public async Task CreateClientAsync_SendsScimJsonContent()
    {
        using var handler = new CapturingHandler(new ClientResponse { Id = Guid.NewGuid().ToString(), ClientId = "orders-service" });
        var apiClient = CreateClient(handler);

        await apiClient.CreateClientAsync(null, new CreateClientRequest { ClientId = "orders-service" }, CancellationToken.None);

        Assert.Equal("application/scim+json", handler.Request!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ListClientsAsync_Filter_EscapesQueryValue()
    {
        using var handler = new CapturingHandler(new ScimListResponse<ClientResponse>());
        var apiClient = CreateClient(handler);

        await apiClient.ListClientsAsync(null, "clientId eq \"orders-service\"", CancellationToken.None);

        Assert.Equal(
            new Uri("https://localhost:5001/scim/v2/Clients?filter=clientId%20eq%20%22orders-service%22"),
            handler.Request!.RequestUri);
    }

    [Fact]
    public async Task GetJwksAsync_EmptyBody_ThrowsApiException()
    {
        using var handler = new CapturingHandler(HttpStatusCode.OK, null);
        var apiClient = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<IdmApiException>(() =>
            apiClient.GetJwksAsync(null, CancellationToken.None));

        Assert.Contains("could not be parsed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJwksAsync_RequestFailure_ThrowsServiceUnavailable()
    {
        using var handler = new ThrowingHandler();
        var apiClient = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<IdmApiException>(() =>
            apiClient.GetJwksAsync(null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("Could not reach the IdM API", exception.Message, StringComparison.Ordinal);
    }

    private static IdmApiClient CreateClient(HttpMessageHandler handler)
    {
        var instances = new IdmApiInstancesOptions
        {
            ["local"] = new IdmApiInstanceOptions
            {
                BaseUrl = new Uri("https://localhost:5001"),
                ClientId = "mcp-local",
                ClientCertificatePath = "/certs/local.pem",
            },
            ["test"] = new IdmApiInstanceOptions
            {
                BaseUrl = new Uri("https://localhost:5003"),
                ClientId = "mcp-test",
                ClientCertificatePath = "/certs/test.pem",
            },
        };
        var resolver = new IdmApiInstanceResolver(
            Options.Create(instances),
            Options.Create(new McpRuntimeOptions { DefaultInstance = "local" }));

        var tokenProvider = Substitute.For<IIdmApiTokenProvider>();
        tokenProvider
            .GetAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("test-bearer-token");

#pragma warning disable CA2000
        var httpClient = new HttpClient(handler, false);
#pragma warning restore CA2000
        return new IdmApiClient(httpClient, resolver, tokenProvider);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _response;

        public CapturingHandler(object response)
            : this(HttpStatusCode.OK, response)
        {
        }

        public CapturingHandler(HttpStatusCode statusCode, object? response)
        {
            this._statusCode = statusCode;
            this._response = response;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Request = request;

            var response = new HttpResponseMessage(this._statusCode);

            if (this._response is not null)
            {
                var json = JsonSerializer.Serialize(this._response, _jsonOptions);
                response.Content = new StringContent(json);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class TextResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _response;

        public TextResponseHandler(HttpStatusCode statusCode, string response)
        {
            this._statusCode = statusCode;
            this._response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(this._statusCode)
            {
                Content = new StringContent(this._response),
            };

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("network unavailable");
        }
    }
}
