namespace Opera2Oris.Entities;

public sealed record BofExportFile(
    string FilePath,
    BofTransactionCategory Category,
    BofExportScope Scope,
    IReadOnlyList<BofExportRecord> Records,
    IReadOnlyList<BofImportWarning> Warnings)
{
    public string FileName => System.IO.Path.GetFileName(FilePath);
}
