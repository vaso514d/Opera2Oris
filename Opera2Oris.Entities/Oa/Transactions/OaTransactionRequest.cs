using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaTransactionRequest
{
    [JsonIgnore]
    public string? SourceFilePath { get; set; }

    [JsonIgnore]
    public long? SourceLineNumber { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("oA_TransactionsID")]
    public long? OaTransactionsId { get; set; }

    [JsonPropertyName("transactionComment")]
    public string? TransactionComment { get; set; }

    [JsonPropertyName("transactionDate")]
    public DateTime? TransactionDate { get; set; }

    [JsonPropertyName("transactionDelay")]
    public bool? TransactionDelay { get; set; }

    [JsonPropertyName("transactionDocumentalConfirm")]
    public bool? TransactionDocumentalConfirm { get; set; }

    [JsonPropertyName("transactionDocumentNumber")]
    public string? TransactionDocumentNumber { get; set; }

    [JsonPropertyName("clearEntries")]
    public bool? ClearEntries { get; set; }

    [JsonPropertyName("correctDisbalance")]
    public bool? CorrectDisbalance { get; set; }

    [JsonPropertyName("transactionEntries")]
    public List<OaTransactionEntryRequest> TransactionEntries { get; set; } = [];
}
