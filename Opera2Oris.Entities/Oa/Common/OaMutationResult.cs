using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaMutationResult
{
    [JsonPropertyName("id")]
    public JsonElement Id { get; init; }

    [JsonPropertyName("idFieldName")]
    public string? IdFieldName { get; init; }

    public long? GetInt64Id()
    {
        if (Id.ValueKind == JsonValueKind.Number && Id.TryGetInt64(out var value))
        {
            return value;
        }

        if (Id.ValueKind == JsonValueKind.String && long.TryParse(Id.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    public string? GetStringId() => Id.ValueKind switch
    {
        JsonValueKind.String => Id.GetString(),
        JsonValueKind.Number => Id.GetRawText(),
        _ => null
    };
}
