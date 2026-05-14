using Backend.Application.Models.Scim;

namespace Backend.Application.Models.Clients;

public sealed class ClientResponse
{
    public IReadOnlyList<string> Schemas { get; init; } =
        ["urn:idmdemo:params:scim:schemas:extension:2.0:Client"];

    public string Id { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool Active { get; init; }

    public ScimMeta Meta { get; init; } = new();
}
