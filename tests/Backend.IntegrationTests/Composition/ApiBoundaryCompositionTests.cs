using Backend.Api.Composition;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.IntegrationTests.Composition;

public sealed class ApiBoundaryCompositionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiBoundaryCompositionTests(TestWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this._factory = factory;
    }

    [Theory]
    [InlineData(".well-known/openid-configuration", "AuthorizationServer")]
    [InlineData(".well-known/jwks.json", "AuthorizationServer")]
    [InlineData("connect/token", "AuthorizationServer")]
    [InlineData("scim/v2/Users", "IdpAdminApi")]
    [InlineData("scim/v2/Certificates/Authority", "IdpAdminApi")]
    public void ControllerRoutes_HaveExpectedApiBoundaryOwnership(string routePattern, string boundaryName)
    {
        _ = this._factory.CreateClient();
        var endpoints = this._factory.Services.GetRequiredService<EndpointDataSource>();

        var matchingEndpoints = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                routePattern,
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(matchingEndpoints);
        foreach (var endpoint in matchingEndpoints)
        {
            var boundary = Assert.Single(endpoint.Metadata.OfType<ApiBoundaryMetadata>());
            Assert.Equal(boundaryName, boundary.Name);
        }
    }
}
