using Backend.Application.Models.Auth;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route(".well-known")]
[Produces("application/json")]
public sealed class AuthorizationServerMetadataController : ControllerBase
{
    private readonly IAuthorizationServerService _authorizationServerService;

    public AuthorizationServerMetadataController(IAuthorizationServerService authorizationServerService)
    {
        this._authorizationServerService = authorizationServerService;
    }

    [HttpGet("openid-configuration")]
    [ProducesResponseType(typeof(DiscoveryResponse), StatusCodes.Status200OK)]
    public IActionResult GetOpenIdConfiguration()
    {
        return this.Ok(this._authorizationServerService.GetDiscovery());
    }

    [HttpGet("jwks.json")]
    [ProducesResponseType(typeof(JwksResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJwksAsync(CancellationToken cancellationToken)
    {
        var response = await this._authorizationServerService.GetJwksAsync(cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }
}
