using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Certificates;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Models.Users;

namespace Backend.Mcp.Api;

public sealed class IdmApiClient : IIdmApiClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IIdmApiInstanceResolver _instanceResolver;
    private readonly IIdmApiTokenProvider _tokenProvider;

    public IdmApiClient(HttpClient httpClient, IIdmApiInstanceResolver instanceResolver, IIdmApiTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(instanceResolver);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        this._httpClient = httpClient;
        this._instanceResolver = instanceResolver;
        this._tokenProvider = tokenProvider;
    }

    public Task<IdmApiCallResult<UserResponse>> CreateUserAsync(
        string? instance,
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<UserResponse>(instance, HttpMethod.Post, "scim/v2/Users", request, cancellationToken);
    }

    public Task<IdmApiCallResult<UserResponse>> GetUserAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<UserResponse>(instance, HttpMethod.Get, $"scim/v2/Users/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<UserResponse>> UpdateUserAsync(
        string? instance,
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<UserResponse>(instance, HttpMethod.Put, $"scim/v2/Users/{id:D}", request, cancellationToken);
    }

    public Task<IdmApiCallResult<EmptyResponse>> DeleteUserAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<EmptyResponse>(instance, HttpMethod.Delete, $"scim/v2/Users/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<ClientResponse>> CreateClientAsync(
        string? instance,
        CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<ClientResponse>(instance, HttpMethod.Post, "scim/v2/Clients", request, cancellationToken);
    }

    public Task<IdmApiCallResult<ClientResponse>> GetClientAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<ClientResponse>(instance, HttpMethod.Get, $"scim/v2/Clients/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<ScimListResponse<ClientResponse>>> ListClientsAsync(
        string? instance,
        string? filter,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(filter)
            ? "scim/v2/Clients"
            : $"scim/v2/Clients?filter={Uri.EscapeDataString(filter)}";

        return this.SendAsync<ScimListResponse<ClientResponse>>(instance, HttpMethod.Get, path, cancellationToken);
    }

    public Task<IdmApiCallResult<ClientResponse>> UpdateClientAsync(
        string? instance,
        Guid id,
        UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<ClientResponse>(instance, HttpMethod.Put, $"scim/v2/Clients/{id:D}", request, cancellationToken);
    }

    public Task<IdmApiCallResult<EmptyResponse>> DeleteClientAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<EmptyResponse>(instance, HttpMethod.Delete, $"scim/v2/Clients/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<RoleResponse>> CreateRoleAsync(
        string? instance,
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<RoleResponse>(instance, HttpMethod.Post, "scim/v2/Roles", request, cancellationToken);
    }

    public Task<IdmApiCallResult<RoleResponse>> UpdateRoleAsync(
        string? instance,
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<RoleResponse>(instance, HttpMethod.Put, $"scim/v2/Roles/{id:D}", request, cancellationToken);
    }

    public Task<IdmApiCallResult<EmptyResponse>> DeleteRoleAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<EmptyResponse>(instance, HttpMethod.Delete, $"scim/v2/Roles/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<ScopeResponse>> CreateScopeAsync(
        string? instance,
        CreateScopeRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<ScopeResponse>(instance, HttpMethod.Post, "scim/v2/Scopes", request, cancellationToken);
    }

    public Task<IdmApiCallResult<ScopeResponse>> UpdateScopeAsync(
        string? instance,
        Guid id,
        UpdateScopeRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<ScopeResponse>(instance, HttpMethod.Put, $"scim/v2/Scopes/{id:D}", request, cancellationToken);
    }

    public Task<IdmApiCallResult<EmptyResponse>> DeleteScopeAsync(
        string? instance,
        Guid id,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<EmptyResponse>(instance, HttpMethod.Delete, $"scim/v2/Scopes/{id:D}", cancellationToken);
    }

    public Task<IdmApiCallResult<CertificateResponse>> CreateCertificateAsync(
        string? instance,
        Guid clientId,
        CreateCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<CertificateResponse>(
            instance,
            HttpMethod.Post,
            $"scim/v2/Clients/{clientId:D}/Certificates",
            request,
            cancellationToken);
    }

    public Task<IdmApiCallResult<ScimListResponse<CertificateResponse>>> ListCertificatesAsync(
        string? instance,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<ScimListResponse<CertificateResponse>>(
            instance,
            HttpMethod.Get,
            $"scim/v2/Clients/{clientId:D}/Certificates",
            cancellationToken);
    }

    public Task<IdmApiCallResult<CertificateResponse>> GetCertificateAsync(
        string? instance,
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<CertificateResponse>(
            instance,
            HttpMethod.Get,
            $"scim/v2/Clients/{clientId:D}/Certificates/{certificateId:D}",
            cancellationToken);
    }

    public Task<IdmApiCallResult<CertificateResponse>> RevokeCertificateAsync(
        string? instance,
        Guid clientId,
        Guid certificateId,
        RevokeCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return this.SendJsonAsync<CertificateResponse>(
            instance,
            HttpMethod.Post,
            $"scim/v2/Clients/{clientId:D}/Certificates/{certificateId:D}/Revoke",
            request,
            cancellationToken);
    }

    public Task<IdmApiCallResult<CertificateAuthorityResponse>> GetCertificateAuthorityAsync(
        string? instance,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<CertificateAuthorityResponse>(
            instance,
            HttpMethod.Get,
            "scim/v2/Certificates/Authority",
            cancellationToken);
    }

    public Task<IdmApiCallResult<DiscoveryResponse>> GetDiscoveryAsync(
        string? instance,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<DiscoveryResponse>(
            instance,
            HttpMethod.Get,
            ".well-known/openid-configuration",
            cancellationToken);
    }

    public Task<IdmApiCallResult<JwksResponse>> GetJwksAsync(
        string? instance,
        CancellationToken cancellationToken)
    {
        return this.SendAsync<JwksResponse>(instance, HttpMethod.Get, ".well-known/jwks.json", cancellationToken);
    }

    private async Task<IdmApiCallResult<T>> SendJsonAsync<T>(
        string? instance,
        HttpMethod method,
        string relativePath,
        object requestBody,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        (HttpRequestMessage Request, string InstanceName, string CorrelationId) prepared;
        try
        {
            prepared = await this.CreateRequestAsync(instance, method, relativePath, cancellationToken).ConfigureAwait(false);
            correlationId = prepared.CorrelationId;
        }
        catch (HttpRequestException exception)
        {
            throw new IdmApiException(
                System.Net.HttpStatusCode.ServiceUnavailable,
                correlationId,
                $"Could not reach the IdM token endpoint. CorrelationId={correlationId}",
                exception);
        }

        using (prepared.Request)
        {
            prepared.Request.Content = JsonContent.Create(requestBody, options: _jsonOptions);
            prepared.Request.Content.Headers.ContentType = new("application/scim+json");
            return await this.SendPreparedAsync<T>(prepared.Request, prepared.InstanceName, correlationId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IdmApiCallResult<T>> SendAsync<T>(
        string? instance,
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        (HttpRequestMessage Request, string InstanceName, string CorrelationId) prepared;
        try
        {
            prepared = await this.CreateRequestAsync(instance, method, relativePath, cancellationToken).ConfigureAwait(false);
            correlationId = prepared.CorrelationId;
        }
        catch (HttpRequestException exception)
        {
            throw new IdmApiException(
                System.Net.HttpStatusCode.ServiceUnavailable,
                correlationId,
                $"Could not reach the IdM token endpoint. CorrelationId={correlationId}",
                exception);
        }

        using var req = prepared.Request;
        return await this.SendPreparedAsync<T>(req, prepared.InstanceName, correlationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(HttpRequestMessage Request, string InstanceName, string CorrelationId)> CreateRequestAsync(
        string? instanceName,
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var resolved = this._instanceResolver.Resolve(instanceName);
        var correlationId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        var token = await this._tokenProvider.GetAccessTokenAsync(resolved.Name, cancellationToken).ConfigureAwait(false);

        var uri = new Uri(resolved.BaseUrl, relativePath);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Accept.ParseAdd("application/json");
        return (request, resolved.Name, correlationId);
    }

    private async Task<IdmApiCallResult<T>> SendPreparedAsync<T>(
        HttpRequestMessage request,
        string instance,
        string correlationId,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new IdmApiException(
                HttpStatusCode.ServiceUnavailable,
                correlationId,
                $"Could not reach the IdM API at {request.RequestUri}. Ensure Backend.Api is running and the selected MCP instance URL is correct. CorrelationId={correlationId}",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = await this.ReadErrorMessageAsync(response, correlationId, cancellationToken).ConfigureAwait(false);
                throw new IdmApiException(response.StatusCode, correlationId, message);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return new IdmApiCallResult<T>(instance, correlationId, (T)(object)new EmptyResponse());
            }

            T? value;
            try
            {
                value = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new IdmApiException(
                    response.StatusCode,
                    correlationId,
                    "The IdM API returned a response body that could not be parsed.",
                    exception);
            }

            if (value is null)
            {
                throw new IdmApiException(response.StatusCode, correlationId, "The IdM API returned an empty response body.");
            }

            return new IdmApiCallResult<T>(instance, correlationId, value);
        }
    }

#pragma warning disable CA1822
    private Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        string correlationId,
        CancellationToken cancellationToken)
#pragma warning restore CA1822
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult($"{(int)response.StatusCode} {response.StatusCode}. CorrelationId={correlationId}");
    }
}
