using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Users;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.Users;

public sealed class UsersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsersApiTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._factory = factory;
        this._client = factory.CreateClient();
        this._client.DefaultRequestHeaders.Add("X-Api-Key", TestWebApplicationFactory.TestApiKey);
    }

    [Fact]
    public async Task Post_ValidUser_Returns201WithLocation()
    {
        var request = new CreateUserRequest { UserName = $"user-{Guid.NewGuid():N}" };

        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var content = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(content, _jsonOptions)!;
        Assert.Equal(request.UserName, user.UserName);
        Assert.True(user.Active);
        Assert.NotEmpty(user.Id);
    }

    [Fact]
    public async Task Post_DuplicateUserName_Returns409()
    {
        var userName = $"dup-{Guid.NewGuid():N}";
        await this.CreateUserAsync(userName);

        var request = new CreateUserRequest { UserName = userName };
        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ScimError>(content, _jsonOptions)!;
        Assert.Equal("uniqueness", error.ScimType);
    }

    [Fact]
    public async Task Post_MissingUserName_Returns400()
    {
        var request = new CreateUserRequest { UserName = string.Empty };

        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ScimError>(content, _jsonOptions)!;
        Assert.Equal("invalidValue", error.ScimType);
    }

    [Fact]
    public async Task Get_ExistingUser_Returns200()
    {
        var created = await this.CreateUserAsync($"get-{Guid.NewGuid():N}");

        var response = await this._client.GetAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(content, _jsonOptions)!;
        Assert.Equal(created.UserName, user.UserName);
    }

    [Fact]
    public async Task Get_NonExistingUser_Returns404()
    {
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Users/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetList_NoFilter_ReturnsAllUsers()
    {
        var name1 = $"list-{Guid.NewGuid():N}";
        var name2 = $"list-{Guid.NewGuid():N}";
        await this.CreateUserAsync(name1);
        await this.CreateUserAsync(name2);

        var response = await this._client.GetAsync(new Uri("/scim/v2/Users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<ScimListResponse<UserResponse>>(content, _jsonOptions)!;
        Assert.True(list.TotalResults >= 2);
    }

    [Fact]
    public async Task GetList_WithUserNameFilter_ReturnsMatchingUser()
    {
        var userName = $"filter-{Guid.NewGuid():N}";
        await this.CreateUserAsync(userName);

        var encoded = Uri.EscapeDataString($"userName eq \"{userName}\"");
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Users?filter={encoded}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<ScimListResponse<UserResponse>>(content, _jsonOptions)!;
        Assert.Equal(1, list.TotalResults);
        Assert.Equal(userName, list.Resources[0].UserName);
    }

    [Fact]
    public async Task Put_ValidUpdate_Returns200()
    {
        var created = await this.CreateUserAsync($"put-{Guid.NewGuid():N}");
        var updateRequest = new UpdateUserRequest
        {
            UserName = created.UserName,
            DisplayName = "Updated Display",
            Active = true,
        };

        var response = await this._client.PutAsJsonAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative), updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(content, _jsonOptions)!;
        Assert.Equal("Updated Display", user.DisplayName);
    }

    [Fact]
    public async Task Patch_ReplaceDisplayName_Returns200()
    {
        var created = await this.CreateUserAsync($"patch-{Guid.NewGuid():N}");
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

        var response = await this._client.PatchAsJsonAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative), patchRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(content, _jsonOptions)!;
        Assert.Equal("Patched Name", user.DisplayName);
    }

    [Fact]
    public async Task Delete_ExistingUser_Returns204()
    {
        var created = await this.CreateUserAsync($"del-{Guid.NewGuid():N}");

        var deleteResponse = await this._client.DeleteAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await this._client.GetAsync(new Uri($"/scim/v2/Users/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Request_WithoutApiKey_Returns401()
    {
        using var anonClient = this._factory.CreateClient();

        var response = await anonClient.GetAsync(new Uri("/scim/v2/Users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        using var anonClient = this._factory.CreateClient();
        anonClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await anonClient.GetAsync(new Uri("/scim/v2/Users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<UserResponse> CreateUserAsync(string userName, bool active = true)
    {
        var request = new CreateUserRequest { UserName = userName, Active = active };
        var response = await this._client.PostAsJsonAsync(new Uri("/scim/v2/Users", UriKind.Relative), request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<UserResponse>(content, _jsonOptions)!;
    }
}
