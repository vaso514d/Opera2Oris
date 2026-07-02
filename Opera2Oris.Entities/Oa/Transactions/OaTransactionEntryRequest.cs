using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaTransactionEntryRequest
{
    [JsonPropertyName("oA_EntriesID")]
    public long? OaEntriesId { get; set; }

    [JsonPropertyName("deleteRow")]
    public bool? DeleteRow { get; set; }

    [JsonPropertyName("mainEntry")]
    public bool? MainEntry { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("debitAmount")]
    public decimal? DebitAmount { get; set; }

    [JsonPropertyName("creditAmount")]
    public decimal? CreditAmount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("debitQuantity")]
    public decimal? DebitQuantity { get; set; }

    [JsonPropertyName("creditQuantity")]
    public decimal? CreditQuantity { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("costCentre")]
    public string? CostCentre { get; set; }

    [JsonPropertyName("costUnit")]
    public string? CostUnit { get; set; }

    [JsonPropertyName("amountCalculationType")]
    public int? AmountCalculationType { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("cashFlow")]
    public string? CashFlow { get; set; }

    [JsonPropertyName("memorial")]
    public int? Memorial { get; set; }

    [JsonPropertyName("rate")]
    public decimal? Rate { get; set; }

    [JsonPropertyName("isRateFixed")]
    public bool? IsRateFixed { get; set; }

    [JsonPropertyName("debitEquivalent")]
    public decimal? DebitEquivalent { get; set; }

    [JsonPropertyName("creditEquivalent")]
    public decimal? CreditEquivalent { get; set; }

    [JsonPropertyName("rate2")]
    public decimal? Rate2 { get; set; }

    [JsonPropertyName("debitEquivalent2")]
    public decimal? DebitEquivalent2 { get; set; }

    [JsonPropertyName("creditEquivalent2")]
    public decimal? CreditEquivalent2 { get; set; }

    [JsonPropertyName("stockRegisterRate")]
    public decimal? StockRegisterRate { get; set; }

    [JsonPropertyName("debitStock")]
    public decimal? DebitStock { get; set; }

    [JsonPropertyName("creditStock")]
    public decimal? CreditStock { get; set; }

    [JsonPropertyName("internalTurnover")]
    public bool? InternalTurnover { get; set; }

    [JsonPropertyName("actionInCaseDisbalance")]
    public int? ActionInCaseDisbalance { get; set; }

    [JsonPropertyName("oA_AccountsID")]
    public long? OaAccountsId { get; set; }

    [JsonPropertyName("oA_CashflowCategoriesID")]
    public long? OaCashflowCategoriesId { get; set; }

    [JsonPropertyName("oA_CurrenciesID")]
    public long? OaCurrenciesId { get; set; }

    [JsonPropertyName("oA_CostCentreID")]
    public long? OaCostCentreId { get; set; }

    [JsonPropertyName("oA_CostUnitID")]
    public long? OaCostUnitId { get; set; }

    [JsonPropertyName("oA_StockUnitsID")]
    public long? OaStockUnitsId { get; set; }

    [JsonPropertyName("relatedOA_EntriesID")]
    public long? RelatedOaEntriesId { get; set; }
}
