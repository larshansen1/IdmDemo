using System.Text.Json.Serialization;

namespace Backend.Application.Models.Scim;

public sealed class ScimPatchRequest
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = [];

    [JsonPropertyName("Operations")]
    public IReadOnlyList<ScimPatchOperation> Operations { get; init; } = [];
}
