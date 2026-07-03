namespace Opera2Oris.Entities;

public sealed class BofTransactionRule
{
    public string? Account { get; init; }

    public IReadOnlyList<string> TransactionCodes { get; init; } = [];

    public IReadOnlyList<string> TransactionSubGroups { get; init; } = [];

    public IReadOnlyList<string> DescriptionKeywords { get; init; } = [];

    public IReadOnlyList<string> RevenueIndicators { get; init; } = [];

    public bool Matches(BofExportRecord record) =>
        record.Category is BofTransactionCategory.Charge or BofTransactionCategory.Package &&
        (ContainsIgnoreCase(TransactionCodes, record.TransactionCode) ||
            ContainsIgnoreCase(DescriptionKeywords, record.TransactionDescription) ||
            ContainsIgnoreCase(TransactionSubGroups, record.TransactionSubGroup) ||
            ContainsIgnoreCase(RevenueIndicators, record.RevenueIndicator));

    private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}
