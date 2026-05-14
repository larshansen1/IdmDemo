using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Application.Models.Scim;

public sealed class ScimPatchOperation
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}
