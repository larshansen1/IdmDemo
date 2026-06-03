using Backend.Application.Models.Auth;
using Xunit;

namespace Backend.Tests.Models.Auth;

public sealed class AuthorizationServerOptionsTests
{
    [Fact]
    public void Defaults_ConstrainAccessTokensWithDpopAndShortLifetime()
    {
        var options = new AuthorizationServerOptions();

        Assert.True(options.RequireDpop);
        Assert.Equal(300, options.AccessTokenLifetimeSeconds);
    }
}
