using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaApiError
{
    [JsonPropertyName("errorNumber")]
    public int? ErrorNumber { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("errorType")]
    public string? ErrorType { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("class")]
    public string? Class { get; init; }

    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; init; }

    [JsonPropertyName("resourceDLL_ForTranslation")]
    public string? ResourceDllForTranslation { get; init; }

    [JsonPropertyName("errorPointer")]
    public int? ErrorPointer { get; init; }

    public override string ToString()
    {
        if (ErrorNumber is null)
        {
            return Description ?? Comment ?? "OA API error";
        }

        return $"{ErrorNumber}: {Description ?? Comment ?? "OA API error"}";
    }
}
