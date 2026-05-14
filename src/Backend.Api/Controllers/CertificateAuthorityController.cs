using Backend.Application.Models.Certificates;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Certificates/Authority")]
[Produces("application/json")]
public sealed class CertificateAuthorityController : ControllerBase
{
    private readonly IMachineClientCertificateService _certificateService;

    public CertificateAuthorityController(IMachineClientCertificateService certificateService)
    {
        this._certificateService = certificateService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CertificateAuthorityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var response = await this._certificateService.GetCertificateAuthorityAsync(cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }
}
