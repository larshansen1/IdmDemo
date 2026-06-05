using System.Net;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Xunit;

namespace Backend.IntegrationTests.OpenApi;

public sealed class SwaggerDocumentTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SwaggerDocumentTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSwaggerJson_ReturnsV1Document()
    {
        using var response = await this._client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal("3.0.4", root.GetProperty("openapi").GetString());
        Assert.Equal("IdmDemo API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());
        Assert.True(root.GetProperty("paths").TryGetProperty("/scim/v2/Users", out _));
    }
}
