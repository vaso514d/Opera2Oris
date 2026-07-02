using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaTransactionIdRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("oA_TransactionsID")]
    public long OaTransactionsId { get; set; }
}
