using System.Text;
using Microsoft.VisualBasic.FileIO;
using Opera2Oris.Entities;

namespace Opera2Oris.Domain;

public sealed class BofExportWorker : IBofExportWorker
{
    public Task<BofExportBatch> ReadAsync(BofExportImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Task.FromResult(Read(options, cancellationToken));
    }

    private static BofExportBatch Read(BofExportImportOptions options, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"BOF export directory was not found: {options.SourceDirectory}");
        }

        if (!File.Exists(options.HeaderDictionaryPath))
        {
            throw new FileNotFoundException("BOF header dictionary was not found.", options.HeaderDictionaryPath);
        }

        var (columns, headerWarnings) = BofHeaderDictionaryReader.Read(options.HeaderDictionaryPath);
        var warnings = headerWarnings.ToList();
        var encoding = options.CreateEncoding();
        var files = new List<BofExportFile>();

        foreach (var filePath in Directory.EnumerateFiles(options.SourceDirectory, options.SearchPattern).OrderBy(file => file))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(ReadFile(filePath, columns, options.Delimiter, encoding, cancellationToken));
        }

        if (files.Count == 0)
        {
            warnings.Add(new BofImportWarning(options.SourceDirectory, null, $"No BOF export files matched '{options.SearchPattern}'."));
        }

        return new BofExportBatch(
            options.SourceDirectory,
            options.HeaderDictionaryPath,
            columns,
            files,
            warnings);
    }

    private static BofExportFile ReadFile(
        string filePath,
        IReadOnlyList<BofColumnDefinition> columns,
        char delimiter,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var (category, scope) = BofExportFileClassifier.Classify(filePath);
        var records = new List<BofExportRecord>();
        var warnings = new List<BofImportWarning>();
        var hasRecordMarker = false;

        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(delimiter.ToString());

        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceLineNumber = parser.LineNumber;
            string[]? values;

            try
            {
                values = parser.ReadFields();
            }
            catch (MalformedLineException exception)
            {
                long? errorLineNumber = parser.ErrorLineNumber > 0
                    ? parser.ErrorLineNumber
                    : sourceLineNumber > 0 ? sourceLineNumber : null;

                warnings.Add(new BofImportWarning(filePath, errorLineNumber, $"Malformed CSV record: {exception.Message}"));
                continue;
            }

            if (values is null || IsEmptyRecord(values))
            {
                continue;
            }

            if (!hasRecordMarker && IsRecordMarker(values))
            {
                hasRecordMarker = true;
                continue;
            }

            if (!hasRecordMarker)
            {
                warnings.Add(new BofImportWarning(filePath, sourceLineNumber, "Missing REC marker before the first data row."));
                hasRecordMarker = true;
            }

            if (values.Length != columns.Count)
            {
                warnings.Add(new BofImportWarning(
                    filePath,
                    sourceLineNumber,
                    $"Expected {columns.Count} fields from header dictionary but found {values.Length}."));

                if (values.Length < columns.Count)
                {
                    continue;
                }
            }

            records.Add(new BofExportRecord(
                filePath,
                sourceLineNumber,
                category,
                scope,
                CreateFieldValues(columns, values)));
        }

        if (!hasRecordMarker)
        {
            warnings.Add(new BofImportWarning(filePath, null, "File does not contain the expected REC marker."));
        }

        return new BofExportFile(filePath, category, scope, records, warnings);
    }

    private static IReadOnlyList<BofFieldValue> CreateFieldValues(IReadOnlyList<BofColumnDefinition> columns, string[] values)
    {
        var fields = new BofFieldValue[columns.Count];

        for (var index = 0; index < columns.Count; index++)
        {
            fields[index] = new BofFieldValue(columns[index], EmptyToNull(values[index]));
        }

        return fields;
    }

    private static bool IsRecordMarker(IReadOnlyList<string> values) =>
        values.Count == 1 && string.Equals(values[0], "REC", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmptyRecord(IReadOnlyList<string> values) =>
        values.Count == 0 || values.All(string.IsNullOrEmpty);

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
