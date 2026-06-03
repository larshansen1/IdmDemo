using Backend.Api;
using Backend.Api.Middleware;
using Backend.Application.Models.Auth;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Backend.IntegrationTests.Middleware;

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
            new AccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: null)),
            new DpopBoundAccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: "jkt")));

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

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = true },
            new AccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: null)),
            new DpopBoundAccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: "jkt")));

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

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = false },
            new AccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: null)),
            new DpopBoundAccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: "jkt")));

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

        await middleware.InvokeAsync(
            context,
            new AuthorizationServerOptions { RequireDpop = false },
            new AccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: "jkt")),
            new DpopBoundAccessTokenValidatorStub(CreateAdminToken(dpopJwkThumbprint: "jkt")));

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

    private sealed class AccessTokenValidatorStub : IAccessTokenValidator
    {
        private readonly ValidatedAccessToken _token;

        public AccessTokenValidatorStub(ValidatedAccessToken token)
        {
            this._token = token;
        }

        public Task<ValidatedAccessToken> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this._token);
        }
    }

    private sealed class DpopBoundAccessTokenValidatorStub : IDpopBoundAccessTokenValidator
    {
        private readonly ValidatedAccessToken _token;

        public DpopBoundAccessTokenValidatorStub(ValidatedAccessToken token)
        {
            this._token = token;
        }

        public Task<ValidatedAccessToken> ValidateAsync(
            string accessToken,
            string dpopProofJwt,
            string httpMethod,
            Uri requestUri,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this._token);
        }
    }
}
