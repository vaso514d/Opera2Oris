namespace Opera2Oris.Entities;

public sealed class BofToOaMappingOptions
{
    public string? GuestLedgerAccount { get; init; }

    public string? PackageLedgerAccount { get; init; }

    public string? DefaultRevenueAccount { get; init; }

    public string? DefaultVatAccount { get; init; }

    public string? DefaultPaymentAccount { get; init; }

    public string? DefaultCurrency { get; init; } = "GEL";

    public string? DefaultCostCentre { get; init; }

    public string? DefaultCostUnit { get; init; }

    public string? CashFlow { get; init; }

    public string DocumentNumberPrefix { get; init; } = "OPERA-";

    public bool UseBusinessDate { get; init; } = true;

    public bool? CorrectDisbalance { get; init; } = false;

    public IReadOnlyDictionary<string, string> RevenueAccountsByTransactionCode { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> RevenueAccountsByTransactionSubGroup { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> PaymentAccountsByTransactionCode { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> PaymentAccountsByMethod { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
