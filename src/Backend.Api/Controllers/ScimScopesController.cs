using Backend.Application.Models.Scim;
using Backend.Application.Models.Scopes;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Scopes")]
[Consumes("application/json", "application/scim+json")]
[Produces("application/json")]
public sealed class ScimScopesController : ControllerBase
{
    private readonly IGlobalScopeService _scopeService;

    public ScimScopesController(IGlobalScopeService scopeService)
    {
        this._scopeService = scopeService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScopeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateScopeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._scopeService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.CreatedAtAction(nameof(this.GetAsync), new { id = Guid.Parse(response.Id) }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ScopeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await this._scopeService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ScimListResponse<ScopeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var response = await this._scopeService.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ScopeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateScopeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._scopeService.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json", "application/scim+json", "application/json-patch+json")]
    [ProducesResponseType(typeof(ScopeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchAsync(
        Guid id,
        [FromBody] ScimPatchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._scopeService.PatchAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await this._scopeService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return this.NoContent();
    }
}
