using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaDateRangeListRequest : OaListRequest
{
    [JsonPropertyName("diapazonStartDate")]
    public DateTime? DiapazonStartDate { get; set; }

    [JsonPropertyName("diapazonEndDate")]
    public DateTime? DiapazonEndDate { get; set; }
}
