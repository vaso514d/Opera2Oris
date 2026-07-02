namespace Opera2Oris.Entities;

public sealed record BofToOaConversionResult(
    IReadOnlyList<OaTransactionRequest> Transactions,
    IReadOnlyList<BofImportWarning> Warnings);
