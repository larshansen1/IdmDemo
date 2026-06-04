using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Models;

public sealed class TokenFormRequest
{
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; init; }

    [FromForm(Name = "client_id")]
    public string? ClientId { get; init; }

    [FromForm(Name = "scope")]
    public string? Scope { get; init; }

    [FromForm(Name = "resource")]
    public string? Resource { get; init; }
}
