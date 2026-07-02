using System.Text;
using System.Text.RegularExpressions;
using Opera2Oris.Entities;

namespace Opera2Oris.Domain;

internal static partial class BofHeaderDictionaryReader
{
    public static (IReadOnlyList<BofColumnDefinition> Columns, IReadOnlyList<BofImportWarning> Warnings) Read(string headerDictionaryPath)
    {
        var warnings = new List<BofImportWarning>();
        var parsedColumns = new List<(int Ordinal, string Name)>();

        foreach (var line in File.ReadLines(headerDictionaryPath))
        {
            var match = HeaderLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            parsedColumns.Add((int.Parse(match.Groups[1].Value), match.Groups[2].Value.Trim()));
        }

        if (parsedColumns.Count == 0)
        {
            warnings.Add(new BofImportWarning(headerDictionaryPath, null, "Header dictionary does not contain any '-- 01 name' entries."));
            return ([], warnings);
        }

        var duplicateOrdinals = parsedColumns
            .GroupBy(column => column.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        foreach (var ordinal in duplicateOrdinals)
        {
            warnings.Add(new BofImportWarning(headerDictionaryPath, null, $"Header dictionary contains duplicate ordinal {ordinal}."));
        }

        var columns = BuildColumns(parsedColumns);
        AddGapWarnings(headerDictionaryPath, columns, warnings);

        return (columns, warnings);
    }

    private static IReadOnlyList<BofColumnDefinition> BuildColumns(IEnumerable<(int Ordinal, string Name)> parsedColumns)
    {
        var usedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var columns = new List<BofColumnDefinition>();

        foreach (var column in parsedColumns.OrderBy(column => column.Ordinal))
        {
            var baseKey = ToPascalCase(column.Name);
            if (baseKey.Length == 0)
            {
                baseKey = $"Column{column.Ordinal:000}";
            }

            usedKeys.TryGetValue(baseKey, out var usageCount);
            usageCount++;
            usedKeys[baseKey] = usageCount;

            var key = usageCount == 1 ? baseKey : $"{baseKey}{usageCount}";
            columns.Add(new BofColumnDefinition(column.Ordinal, column.Name, key));
        }

        return columns;
    }

    private static void AddGapWarnings(string headerDictionaryPath, IReadOnlyList<BofColumnDefinition> columns, List<BofImportWarning> warnings)
    {
        var expectedOrdinal = 1;
        foreach (var column in columns)
        {
            if (column.Ordinal != expectedOrdinal)
            {
                warnings.Add(new BofImportWarning(
                    headerDictionaryPath,
                    null,
                    $"Header dictionary jumps from expected ordinal {expectedOrdinal} to {column.Ordinal}."));
            }

            expectedOrdinal = column.Ordinal + 1;
        }
    }

    private static string ToPascalCase(string value)
    {
        var builder = new StringBuilder(value.Length);
        var nextUpper = true;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                nextUpper = true;
                continue;
            }

            builder.Append(nextUpper ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            nextUpper = false;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^--\s*(\d+)\s+(.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex HeaderLineRegex();
}
