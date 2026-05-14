using Backend.Application.Models.Scim;
using Backend.Application.Models.Users;
using Backend.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("scim/v2/Users")]
[Consumes("application/json", "application/scim+json")]
[Produces("application/json")]
public sealed class ScimUsersController : ControllerBase
{
    private readonly IUserService _userService;

    public ScimUsersController(IUserService userService)
    {
        this._userService = userService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._userService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.CreatedAtAction(nameof(this.GetAsync), new { id = Guid.Parse(response.Id) }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await this._userService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ScimListResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var response = await this._userService.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._userService.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json", "application/scim+json", "application/json-patch+json")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchAsync(
        Guid id,
        [FromBody] ScimPatchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await this._userService.PatchAsync(id, request, cancellationToken).ConfigureAwait(false);
        return this.Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ScimError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await this._userService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return this.NoContent();
    }
}
