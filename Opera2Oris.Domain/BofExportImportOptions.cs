using System.Text;

namespace Opera2Oris.Domain;

public sealed class BofExportImportOptions
{
    public BofExportImportOptions(string sourceDirectory, string headerDictionaryPath)
    {
        SourceDirectory = sourceDirectory;
        HeaderDictionaryPath = headerDictionaryPath;
    }

    public string SourceDirectory { get; init; }

    public string HeaderDictionaryPath { get; init; }

    public string SearchPattern { get; init; } = "*.csv";

    public char Delimiter { get; init; } = ';';

    public string EncodingName { get; init; } = "windows-1251";

    public Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(EncodingName);
    }
}
