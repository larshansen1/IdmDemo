using Backend.Api.Models;
using Backend.Api.Services;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers;

[ApiController]
[Route("connect/token")]
[Consumes("application/x-www-form-urlencoded")]
[Produces("application/json")]
public sealed class TokenController : ControllerBase
{
    private const string _dpopHeader = "DPoP";

    private readonly IAuthorizationServerService _authorizationServerService;
    private readonly IClientCertificateReader _clientCertificateReader;

    public TokenController(
        IAuthorizationServerService authorizationServerService,
        IClientCertificateReader clientCertificateReader)
    {
        this._authorizationServerService = authorizationServerService;
        this._clientCertificateReader = clientCertificateReader;
    }

    [HttpPost]
    [EnableRateLimiting("token-endpoint-per-ip")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAsync(
        [FromForm] TokenFormRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var certificate = this._clientCertificateReader.Read(this.HttpContext);
            this.Request.Headers.TryGetValue(_dpopHeader, out var dpopProof);
            var response = await this._authorizationServerService.IssueClientCredentialsTokenAsync(
                request.GrantType,
                request.ClientId,
                request.Scope,
                certificate,
                dpopProof.ToString(),
                request.Resource,
                cancellationToken).ConfigureAwait(false);

            return this.Ok(response);
        }
        catch (OAuthException ex)
        {
            return this.StatusCode(
                ex.StatusCode,
                new OAuthErrorResponse
                {
                    Error = ex.Error,
                    ErrorDescription = ex.Description,
                });
        }
    }
}
