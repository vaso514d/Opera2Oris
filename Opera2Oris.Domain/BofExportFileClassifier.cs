using Opera2Oris.Entities;

namespace Opera2Oris.Domain;

internal static class BofExportFileClassifier
{
    public static (BofTransactionCategory Category, BofExportScope Scope) Classify(string filePath)
    {
        var suffix = GetSuffix(filePath);

        if (suffix.Length != 2)
        {
            return (BofTransactionCategory.Unknown, BofExportScope.Unknown);
        }

        var category = suffix[0] switch
        {
            'C' => BofTransactionCategory.Charge,
            'K' => BofTransactionCategory.Package,
            'P' => BofTransactionCategory.Payment,
            _ => BofTransactionCategory.Unknown
        };

        var scope = suffix[1] switch
        {
            'D' => BofExportScope.Daily,
            'O' => BofExportScope.CheckOut,
            'U' => BofExportScope.UntilToday,
            _ => BofExportScope.Unknown
        };

        return (category, scope);
    }

    private static string GetSuffix(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath).ToUpperInvariant();
        var underscoreIndex = name.LastIndexOf('_');
        var code = underscoreIndex >= 0 ? name[(underscoreIndex + 1)..] : name;

        return code.Length >= 2 ? code[^2..] : string.Empty;
    }
}
