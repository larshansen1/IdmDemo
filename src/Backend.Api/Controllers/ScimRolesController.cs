using Backend.Application.Models.Roles;
using Backend.Application.Models.Scim;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Roles")]
[Consumes("application/json", "application/scim+json")]
[Produces("application/json")]
public sealed class ScimRolesController : ControllerBase
{
    private readonly IGlobalRoleService _roleService;

    public ScimRolesController(IGlobalRoleService roleService)
    {
        this._roleService = roleService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._roleService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.CreatedAtAction(nameof(this.GetAsync), new { id = Guid.Parse(response.Id) }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await this._roleService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ScimListResponse<RoleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var response = await this._roleService.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._roleService.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json", "application/scim+json", "application/json-patch+json")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchAsync(
        Guid id,
        [FromBody] ScimPatchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._roleService.PatchAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await this._roleService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return this.NoContent();
    }
}
