using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Backend.Mcp;
using Backend.Mcp.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Mcp;

public sealed class McpHostedAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NonMcpRequestBypassesAuthentication()
    {
        var nextCalled = false;
        var middleware = new McpHostedAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_McpRequestWithoutAuthorization_ReturnsUnauthorized()
    {
        var nextCalled = false;
        var middleware = new McpHostedAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData(" token")]
    public async Task InvokeAsync_McpRequestWithMalformedAuthorization_ReturnsUnauthorized(string authorization)
    {
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = authorization;

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("DPoP, Bearer", context.Response.Headers.WWWAuthenticate);
    }

    [Fact]
    public async Task InvokeAsync_DpopRequiredWithBearerToken_ReturnsUnauthorized()
    {
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DpopRequiredWithMissingProof_ReturnsUnauthorized()
    {
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "DPoP token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DpopValidatorRejectsToken_ReturnsUnauthorized()
    {
        var boundValidator = Substitute.For<IDpopBoundAccessTokenValidator>();
        boundValidator
            .ValidateAsync("token", "proof", "POST", Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ValidatedAccessToken>(
                new OAuthException("invalid_token", "invalid token", StatusCodes.Status401Unauthorized)));
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "DPoP token";
        context.Request.Headers["DPoP"] = "proof";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            boundValidator);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DpopRequiredWithValidDpopToken_AuthenticatesCaller()
    {
        var nextCalled = false;
        var boundValidator = Substitute.For<IDpopBoundAccessTokenValidator>();
        boundValidator
            .ValidateAsync("token", "proof", "POST", Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Read));

        var middleware = new McpHostedAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Headers.Authorization = "DPoP token";
        context.Request.Headers["DPoP"] = "proof";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            boundValidator);

        Assert.True(nextCalled);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Contains(context.User.FindAll("scope"), claim => claim.Value == McpScopes.Read);
        var callerContext = context.Items[typeof(McpCallerContext)] as McpCallerContext;
        Assert.NotNull(callerContext);
        Assert.Equal("subject", callerContext.Subject);
        Assert.Equal("client", callerContext.ClientId);
        Assert.Contains(McpScopes.Read, callerContext.Scopes);
    }

    [Fact]
    public async Task InvokeAsync_DpopRequiredPassesCanonicalRequestUriToValidator()
    {
        Uri? validatedUri = null;
        var boundValidator = Substitute.For<IDpopBoundAccessTokenValidator>();
        boundValidator
            .ValidateAsync("token", "proof", "POST", Arg.Do<Uri>(uri => validatedUri = uri), Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Read));

        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Host = new HostString("mcp.test", 5100);
        context.Request.PathBase = "/idm";
        context.Request.Path = "/mcp";
        context.Request.QueryString = new QueryString("?transport=streamable-http");
        context.Request.Headers.Authorization = "DPoP token";
        context.Request.Headers["DPoP"] = "proof";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.HostedProduction }),
            Substitute.For<IAccessTokenValidator>(),
            boundValidator);

        Assert.Equal(new Uri("https://mcp.test:5100/idm/mcp?transport=streamable-http"), validatedUri);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentBearerWithDpopBoundToken_ReturnsUnauthorized()
    {
        var tokenValidator = Substitute.For<IAccessTokenValidator>();
        tokenValidator
            .ValidateAsync("token", Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Read, dpopThumbprint: "jkt"));

        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            tokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentBearerAllowed_AuthenticatesCaller()
    {
        var nextCalled = false;
        var tokenValidator = Substitute.For<IAccessTokenValidator>();
        tokenValidator
            .ValidateAsync("token", Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Read, certificateThumbprint: "x5t"));

        var middleware = new McpHostedAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer token";
        context.Request.Headers["X-Api-Key"] = "caller-supplied-key";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions
            {
                Profile = McpProfile.LocalHostedDevelopment,
            }),
            tokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.True(nextCalled);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Empty(context.User.FindAll("X-Api-Key"));
        Assert.Empty(context.User.FindAll("x-api-key"));
        await tokenValidator.Received(1).ValidateAsync("token", Arg.Any<CancellationToken>());
        var callerContext = context.Items[typeof(McpCallerContext)] as McpCallerContext;
        Assert.NotNull(callerContext);
        Assert.Equal("subject", callerContext.Subject);
        Assert.Equal("client", callerContext.ClientId);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentDpopTokenWithMissingProof_ReturnsUnauthorized()
    {
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "DPoP token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentUnsupportedScheme_ReturnsUnauthorized()
    {
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "Basic token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            Substitute.For<IAccessTokenValidator>(),
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentBearerValidatorRejectsToken_ReturnsUnauthorized()
    {
        var tokenValidator = Substitute.For<IAccessTokenValidator>();
        tokenValidator
            .ValidateAsync("token", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ValidatedAccessToken>(
                new OAuthException("invalid_token", "invalid token", StatusCodes.Status401Unauthorized)));
        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            tokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentDpopTokenUsesBoundValidator()
    {
        var boundValidator = Substitute.For<IDpopBoundAccessTokenValidator>();
        boundValidator
            .ValidateAsync("token", "proof", "POST", Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Read));

        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "DPoP token";
        context.Request.Headers["DPoP"] = "proof";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            Substitute.For<IAccessTokenValidator>(),
            boundValidator);

        Assert.True(context.User.Identity?.IsAuthenticated);
        await boundValidator.Received(1)
            .ValidateAsync("token", "proof", "POST", Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_TokenSignedWithUnknownJwksKey_ReturnsUnauthorized()
    {
        using var trustedKey = RSA.Create(2048);
        using var unknownKey = RSA.Create(2048);
        var apiClient = Substitute.For<IIdmApiClient>();
        apiClient
            .GetJwksAsync(null, Arg.Any<CancellationToken>())
            .Returns(new IdmApiCallResult<JwksResponse>(
                "local",
                "correlation-id",
                CreateJwksResponse(trustedKey, "trusted-key")));
        var tokenValidator = new AccessTokenValidator(
            new AuthorizationServerOptions
            {
                Issuer = "https://issuer.test",
                Audience = "api://mcp",
            },
            new JwksJwtSigningKeyStore(apiClient));

        var nextCalled = false;
        var middleware = new McpHostedAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Headers.Authorization = $"Bearer {CreateAccessToken(unknownKey, "unknown-key")}";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            tokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ValidatedTokenAddsCertificateConfirmationClaim()
    {
        var tokenValidator = Substitute.For<IAccessTokenValidator>();
        tokenValidator
            .ValidateAsync("token", Arg.Any<CancellationToken>())
            .Returns(CreateValidatedToken(McpScopes.Certificates, certificateThumbprint: "x5t"));

        var middleware = new McpHostedAuthenticationMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer token";

        await middleware.InvokeAsync(
            context,
            Options.Create(new McpRuntimeOptions { Profile = McpProfile.LocalHostedDevelopment }),
            tokenValidator,
            Substitute.For<IDpopBoundAccessTokenValidator>());

        Assert.Contains(context.User.FindAll("cnf_x5t_s256"), claim => claim.Value == "x5t");
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.test");
        context.Request.Method = "POST";
        context.Request.Path = "/mcp";
        return context;
    }

    private static ValidatedAccessToken CreateValidatedToken(
        string scope,
        string? certificateThumbprint = null,
        string? dpopThumbprint = null)
    {
        return new ValidatedAccessToken
        {
            Subject = "subject",
            ClientId = "client",
            Scope = scope,
            DpopJwkThumbprint = dpopThumbprint,
            CertificateThumbprintSha256 = certificateThumbprint,
        };
    }

    private static JwksResponse CreateJwksResponse(RSA rsa, string keyId)
    {
        var parameters = rsa.ExportParameters(false);
        return new JwksResponse
        {
            Keys =
            [
                new JsonWebKeyResponse
                {
                    KeyId = keyId,
                    Modulus = Base64UrlEncode(parameters.Modulus ?? []),
                    Exponent = Base64UrlEncode(parameters.Exponent ?? []),
                },
            ],
        };
    }

    private static string CreateAccessToken(RSA rsa, string keyId)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "RS256",
            ["typ"] = "at+jwt",
            ["kid"] = keyId,
        };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = "https://issuer.test",
            ["aud"] = "api://mcp",
            ["sub"] = "subject",
            ["client_id"] = "client",
            ["scope"] = McpScopes.Read,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["cnf"] = new Dictionary<string, object?>
            {
                ["x5t#S256"] = "certificate-thumbprint",
            },
        };
        var signingInput = string.Create(
            null,
            $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}");
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return string.Create(null, $"{signingInput}.{Base64UrlEncode(signature)}");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }
}
