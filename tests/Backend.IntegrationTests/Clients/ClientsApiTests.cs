using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.Clients;

public sealed class ClientsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClientsApiTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._factory = factory;
        this._client = factory.CreateClient();
        this._client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.AdminBearerToken);
    }

    [Fact]
    public async Task Post_ValidClient_Returns201WithLocation()
    {
        var request = new CreateClientRequest { ClientId = $"svc-{Guid.NewGuid():N}" };

        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var content = await response.Content.ReadAsStringAsync();
        var client = JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
        Assert.Equal(request.ClientId, client.ClientId);
        Assert.True(client.Active);
        Assert.NotEmpty(client.Id);
    }

    [Fact]
    public async Task Post_DuplicateClientId_Returns409()
    {
        var clientId = $"dup-{Guid.NewGuid():N}";
        await this.CreateClientAsync(clientId);

        var request = new CreateClientRequest { ClientId = clientId };
        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ScimError>(content, _jsonOptions)!;
        Assert.Equal("uniqueness", error.ScimType);
    }

    [Fact]
    public async Task Post_MissingClientId_Returns400()
    {
        var request = new CreateClientRequest { ClientId = string.Empty };

        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ScimError>(content, _jsonOptions)!;
        Assert.Equal("invalidValue", error.ScimType);
    }

    [Fact]
    public async Task Get_ExistingClient_Returns200()
    {
        var created = await this.CreateClientAsync($"get-{Guid.NewGuid():N}");

        var response = await this._client.GetAsync(new Uri($"/scim/v2/Clients/{created.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var client = JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
        Assert.Equal(created.ClientId, client.ClientId);
    }

    [Fact]
    public async Task Get_NonExistingClient_Returns404()
    {
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Clients/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetList_NoFilter_ReturnsAllClients()
    {
        var id1 = $"list-{Guid.NewGuid():N}";
        var id2 = $"list-{Guid.NewGuid():N}";
        await this.CreateClientAsync(id1);
        await this.CreateClientAsync(id2);

        var response = await this._client.GetAsync(new Uri("/scim/v2/Clients", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<ScimListResponse<ClientResponse>>(content, _jsonOptions)!;
        Assert.True(list.TotalResults >= 2);
    }

    [Fact]
    public async Task GetList_WithClientIdFilter_ReturnsMatchingClient()
    {
        var clientId = $"filter-{Guid.NewGuid():N}";
        await this.CreateClientAsync(clientId);

        var encoded = Uri.EscapeDataString($"clientId eq \"{clientId}\"");
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Clients?filter={encoded}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<ScimListResponse<ClientResponse>>(content, _jsonOptions)!;
        Assert.Equal(1, list.TotalResults);
        Assert.Equal(clientId, list.Resources[0].ClientId);
    }

    [Fact]
    public async Task Put_ValidUpdate_Returns200()
    {
        var created = await this.CreateClientAsync($"put-{Guid.NewGuid():N}");
        var updateRequest = new UpdateClientRequest
        {
            ClientId = created.ClientId,
            DisplayName = "Updated Display",
            Active = true,
        };

        var response = await this._client.PutAsJsonAsync(new Uri($"/scim/v2/Clients/{created.Id}", UriKind.Relative), updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var client = JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
        Assert.Equal("Updated Display", client.DisplayName);
    }

    [Fact]
    public async Task Patch_ReplaceDisplayName_Returns200()
    {
        var created = await this.CreateClientAsync($"patch-{Guid.NewGuid():N}");
        var patchRequest = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace",
                    Path = "displayName",
                    Value = JsonSerializer.SerializeToElement("Patched Name"),
                },
            ],
        };

        var response = await this._client.PatchAsJsonAsync(new Uri($"/scim/v2/Clients/{created.Id}", UriKind.Relative), patchRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var client = JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
        Assert.Equal("Patched Name", client.DisplayName);
    }

    [Fact]
    public async Task Delete_ExistingClient_Returns204()
    {
        var created = await this.CreateClientAsync($"del-{Guid.NewGuid():N}");

        var deleteResponse = await this._client.DeleteAsync(new Uri($"/scim/v2/Clients/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await this._client.GetAsync(new Uri($"/scim/v2/Clients/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Request_WithoutApiKey_Returns401()
    {
        using var anonClient = this._factory.CreateClient();

        var response = await anonClient.GetAsync(new Uri("/scim/v2/Clients", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        using var anonClient = this._factory.CreateClient();
        anonClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await anonClient.GetAsync(new Uri("/scim/v2/Clients", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<ClientResponse> CreateClientAsync(string clientId, bool active = true)
    {
        var request = new CreateClientRequest { ClientId = clientId, Active = active };
        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Clients", UriKind.Relative), request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ClientResponse>(content, _jsonOptions)!;
    }
}
