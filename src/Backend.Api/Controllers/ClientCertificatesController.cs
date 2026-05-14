using Backend.Application.Models.Certificates;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Clients/{clientId:guid}/Certificates")]
[Consumes("application/json", "application/scim+json")]
[Produces("application/json")]
public sealed class ClientCertificatesController : ControllerBase
{
    private readonly IMachineClientCertificateService _certificateService;

    public ClientCertificatesController(IMachineClientCertificateService certificateService)
    {
        this._certificateService = certificateService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CertificateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._certificateService.CreateAsync(clientId, request, cancellationToken).ConfigureAwait(false);
        return this.CreatedAtAction(
            nameof(this.GetAsync),
            new { clientId, certificateId = Guid.Parse(response.Id) },
            response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ScimListResponse<CertificateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var response = await this._certificateService.ListAsync(clientId, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpGet("{certificateId:guid}")]
    [ProducesResponseType(typeof(CertificateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid clientId,
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var response = await this._certificateService.GetAsync(clientId, certificateId, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPost("{certificateId:guid}/Revoke")]
    [ProducesResponseType(typeof(CertificateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(
        Guid clientId,
        Guid certificateId,
        [FromBody] RevokeCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._certificateService
            .RevokeAsync(clientId, certificateId, request, cancellationToken)
            .ConfigureAwait(false);

        return this.Ok(response);
    }
}
