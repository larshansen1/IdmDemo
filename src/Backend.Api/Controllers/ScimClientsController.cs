using Backend.Application.Models.Clients;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Clients")]
[Consumes("application/json", "application/scim+json")]
[Produces("application/json")]
public sealed class ScimClientsController : ControllerBase
{
    private readonly IMachineClientService _clientService;

    public ScimClientsController(IMachineClientService clientService)
    {
        this._clientService = clientService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ClientResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._clientService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.CreatedAtAction(nameof(this.GetAsync), new { id = Guid.Parse(response.Id) }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ClientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await this._clientService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ScimListResponse<ClientResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var response = await this._clientService.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ClientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._clientService.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json", "application/scim+json", "application/json-patch+json")]
    [ProducesResponseType(typeof(ClientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchAsync(
        Guid id,
        [FromBody] ScimPatchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._clientService.PatchAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await this._clientService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return this.NoContent();
    }
}
