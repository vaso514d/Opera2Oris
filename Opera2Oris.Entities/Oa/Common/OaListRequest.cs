using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public class OaListRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("topRecordsCount")]
    public int? TopRecordsCount { get; set; }

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("sort")]
    public string? Sort { get; set; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; set; }
}
