using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Application.Models.Clients;
using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Models.Users;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.AccessManagement;

public sealed class AccessManagementApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;

    public AccessManagementApiTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._client = factory.CreateClient();
        this._client.DefaultRequestHeaders.Add("X-Api-Key", TestWebApplicationFactory.TestApiKey);
    }

    [Fact]
    public async Task Roles_CreateAndFilter_ReturnsRole()
    {
        var value = $"role-{Guid.NewGuid():N}";
        var created = await this.CreateRoleAsync(value);

        var encoded = Uri.EscapeDataString($"value eq \"{value}\"");
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Roles?filter={encoded}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadJsonAsync<ScimListResponse<RoleResponse>>(response);
        Assert.Equal(1, list.TotalResults);
        Assert.Equal(created.Id, list.Resources[0].Id);
        Assert.Equal(value, list.Resources[0].Value);
    }

    [Fact]
    public async Task Roles_UpdatePatchAndDelete_ReturnExpectedStatuses()
    {
        var created = await this.CreateRoleAsync($"role-{Guid.NewGuid():N}");

        var updateResponse = await this._client.PutAsJsonAsync(
            new Uri($"/scim/v2/Roles/{created.Id}", UriKind.Relative),
            new UpdateRoleRequest
            {
                Value = created.Value,
                DisplayName = "Updated Role",
                Description = "Updated role description",
                Active = true,
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadJsonAsync<RoleResponse>(updateResponse);
        Assert.Equal("Updated Role", updated.DisplayName);

        var patchResponse = await this._client.PatchAsJsonAsync(
            new Uri($"/scim/v2/Roles/{created.Id}", UriKind.Relative),
            new ScimPatchRequest
            {
                Operations =
                [
                    Patch("description", "Patched role description"),
                    Patch("active", false),
                ],
            });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await ReadJsonAsync<RoleResponse>(patchResponse);
        Assert.False(patched.Active);
        Assert.Equal("Patched role description", patched.Description);

        var deleteResponse = await this._client.DeleteAsync(new Uri($"/scim/v2/Roles/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Scopes_CreateAndFilter_ReturnsScope()
    {
        var value = $"scope-{Guid.NewGuid():N}";
        var created = await this.CreateScopeAsync(value);

        var encoded = Uri.EscapeDataString($"value eq \"{value}\"");
        var response = await this._client.GetAsync(new Uri($"/scim/v2/Scopes?filter={encoded}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadJsonAsync<ScimListResponse<ScopeResponse>>(response);
        Assert.Equal(1, list.TotalResults);
        Assert.Equal(created.Id, list.Resources[0].Id);
        Assert.Equal(value, list.Resources[0].Value);
    }

    [Fact]
    public async Task Scopes_UpdatePatchAndDelete_ReturnExpectedStatuses()
    {
        var created = await this.CreateScopeAsync($"scope-{Guid.NewGuid():N}");

        var updateResponse = await this._client.PutAsJsonAsync(
            new Uri($"/scim/v2/Scopes/{created.Id}", UriKind.Relative),
            new UpdateScopeRequest
            {
                Value = created.Value,
                DisplayName = "Updated Scope",
                Description = "Updated scope description",
                Active = true,
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadJsonAsync<ScopeResponse>(updateResponse);
        Assert.Equal("Updated Scope", updated.DisplayName);

        var patchResponse = await this._client.PatchAsJsonAsync(
            new Uri($"/scim/v2/Scopes/{created.Id}", UriKind.Relative),
            new ScimPatchRequest
            {
                Operations =
                [
                    Patch("description", "Patched scope description"),
                    Patch("active", false),
                ],
            });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await ReadJsonAsync<ScopeResponse>(patchResponse);
        Assert.False(patched.Active);
        Assert.Equal("Patched scope description", patched.Description);

        var deleteResponse = await this._client.DeleteAsync(new Uri($"/scim/v2/Scopes/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Roles_DeleteAssignedRole_Returns409()
    {
        var role = await this.CreateRoleAsync($"role-{Guid.NewGuid():N}");
        var userResponse = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Users", UriKind.Relative),
            new CreateUserRequest
            {
                UserName = $"user-{Guid.NewGuid():N}",
                AssignedRoles = [role.Value],
            });
        userResponse.EnsureSuccessStatusCode();

        var response = await this._client.DeleteAsync(new Uri($"/scim/v2/Roles/{role.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Scopes_DeleteAssignedScope_Returns409()
    {
        var scope = await this.CreateScopeAsync($"scope-{Guid.NewGuid():N}");
        var clientResponse = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Clients", UriKind.Relative),
            new CreateClientRequest
            {
                ClientId = $"client-{Guid.NewGuid():N}",
                AssignedScopes = [scope.Value],
            });
        clientResponse.EnsureSuccessStatusCode();

        var response = await this._client.DeleteAsync(new Uri($"/scim/v2/Scopes/{scope.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Clients_AssignUnknownScope_Returns400()
    {
        var response = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Clients", UriKind.Relative),
            new CreateClientRequest
            {
                ClientId = $"client-{Guid.NewGuid():N}",
                AssignedScopes = [$"missing-{Guid.NewGuid():N}"],
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_AssignUnknownRole_Returns400()
    {
        var response = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Users", UriKind.Relative),
            new CreateUserRequest
            {
                UserName = $"user-{Guid.NewGuid():N}",
                AssignedRoles = [$"missing-{Guid.NewGuid():N}"],
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, _jsonOptions)!;
    }

    private static ScimPatchOperation Patch<T>(string path, T value)
    {
        return new ScimPatchOperation
        {
            Op = "replace",
            Path = path,
            Value = JsonSerializer.SerializeToElement(value),
        };
    }

    private async Task<RoleResponse> CreateRoleAsync(string value)
    {
        var response = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Roles", UriKind.Relative),
            new CreateRoleRequest { Value = value });
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<RoleResponse>(response);
    }

    private async Task<ScopeResponse> CreateScopeAsync(string value)
    {
        var response = await this._client.PostAsJsonAsync(
            new Uri("/scim/v2/Scopes", UriKind.Relative),
            new CreateScopeRequest { Value = value });
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<ScopeResponse>(response);
    }
}
