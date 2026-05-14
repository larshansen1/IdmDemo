using Backend.Api.Models;
using Backend.Api.Services;
using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("connect/token")]
[Consumes("application/x-www-form-urlencoded")]
[Produces("application/json")]
public sealed class TokenController : ControllerBase
{
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
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateAsync(
        [FromForm] TokenFormRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var certificate = this._clientCertificateReader.Read(this.HttpContext);
            var response = await this._authorizationServerService.IssueClientCredentialsTokenAsync(
                request.GrantType,
                request.ClientId,
                request.Scope,
                certificate,
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
