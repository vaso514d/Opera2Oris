namespace Opera2Oris.Entities;

public sealed record BofExportBatch(
    string SourceDirectory,
    string HeaderDictionaryPath,
    IReadOnlyList<BofColumnDefinition> Columns,
    IReadOnlyList<BofExportFile> Files,
    IReadOnlyList<BofImportWarning> Warnings)
{
    public int RecordCount => Files.Sum(file => file.Records.Count);

    public int WarningCount => Warnings.Count + Files.Sum(file => file.Warnings.Count);

    public IEnumerable<BofImportWarning> AllWarnings => Warnings.Concat(Files.SelectMany(file => file.Warnings));
}
