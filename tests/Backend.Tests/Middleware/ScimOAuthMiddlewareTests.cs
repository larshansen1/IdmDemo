using Backend.Api;
using Backend.Api.Middleware;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Middleware;

public sealed class ScimOAuthMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RequireDpopWithBearerToken_ReturnsUnauthorized()
    {
        var nextCalled = false;
        var middleware = new ScimOAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("Bearer access-token");

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = true },
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RequireDpopWithValidDpopProof_CallsNext()
    {
        var nextCalled = false;
        var middleware = new ScimOAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("DPoP access-token");
        context.Request.Headers["DPoP"] = "proof";
        var boundValidator = Substitute.For<IDpopBoundAccessTokenValidator>();
        boundValidator
            .ValidateAsync(
                "access-token",
                "proof",
                "GET",
                new Uri("https://idmdemo.test/scim/v2/Users"),
                Arg.Any<CancellationToken>())
            .Returns(CreateAdminToken(dpopJwkThumbprint: "jkt"));

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = true },
            Substitute.For<IAccessTokenValidator>(),
            boundValidator);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_BearerAllowedWithPlainAdminToken_CallsNext()
    {
        var nextCalled = false;
        var middleware = new ScimOAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("Bearer access-token");
        var accessTokenValidator = Substitute.For<IAccessTokenValidator>();
        accessTokenValidator
            .ValidateAsync("access-token", Arg.Any<CancellationToken>())
            .Returns(CreateAdminToken(dpopJwkThumbprint: null));

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = false },
            accessTokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_BearerAllowedWithDpopBoundToken_ReturnsUnauthorized()
    {
        var nextCalled = false;
        var middleware = new ScimOAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("Bearer access-token");
        var accessTokenValidator = Substitute.For<IAccessTokenValidator>();
        accessTokenValidator
            .ValidateAsync("access-token", Arg.Any<CancellationToken>())
            .Returns(CreateAdminToken(dpopJwkThumbprint: "jkt"));

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = false },
            accessTokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("idmdemo.test");
        context.Request.Path = "/scim/v2/Users";
        context.Request.Headers.Authorization = authorization;
        return context;
    }

    private static ValidatedAccessToken CreateAdminToken(string? dpopJwkThumbprint)
    {
        return new ValidatedAccessToken
        {
            Subject = "idm-admin",
            ClientId = "idm-admin",
            Roles = [ScimAdminRoles.Admin],
            DpopJwkThumbprint = dpopJwkThumbprint,
        };
    }
}
