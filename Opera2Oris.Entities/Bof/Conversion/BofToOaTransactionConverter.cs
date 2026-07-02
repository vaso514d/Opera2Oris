using System.Globalization;

namespace Opera2Oris.Entities;

public sealed class BofToOaTransactionConverter
{
    public BofToOaConversionResult Convert(BofExportBatch batch, BofToOaMappingOptions options, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return Convert(batch.Files.SelectMany(file => file.Records), options, token);
    }

    public BofToOaConversionResult Convert(IEnumerable<BofExportRecord> records, BofToOaMappingOptions options, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(options);

        var transactions = new List<OaTransactionRequest>();
        var warnings = new List<BofImportWarning>();

        foreach (var record in records)
        {
            var transaction = ConvertRecord(record, options, token, warnings);
            if (transaction is not null)
            {
                transactions.Add(transaction);
            }
        }

        return new BofToOaConversionResult(transactions, warnings);
    }

    private static OaTransactionRequest? ConvertRecord(
        BofExportRecord record,
        BofToOaMappingOptions options,
        string? token,
        List<BofImportWarning> warnings)
    {
        var amount = ResolveAmount(record);
        if (amount is null || amount == 0)
        {
            warnings.Add(CreateWarning(record, "Record does not contain a non-zero amount for OA transaction upload."));
            return null;
        }

        var account = ResolveEntryAccount(record, options);
        if (string.IsNullOrWhiteSpace(account))
        {
            warnings.Add(CreateWarning(
                record,
                $"Missing account mapping for category {record.Category}, transaction code '{record.TransactionCode}', payment method '{record.PaymentMethod}'."));
            return null;
        }

        var absoluteAmount = Math.Abs(amount.Value);
        var currency = FirstValue(record.Currency, options.DefaultCurrency);
        var transactionComment = BuildTransactionComment(record);
        var entryComment = BuildEntryComment(record, transactionComment);

        return new OaTransactionRequest
        {
            Token = token,
            SourceFilePath = record.SourceFilePath,
            SourceLineNumber = record.SourceLineNumber,
            TransactionDate = ResolveTransactionDateTime(record, options),
            TransactionDocumentNumber = BuildDocumentNumber(record, options),
            TransactionComment = transactionComment,
            CorrectDisbalance = options.CorrectDisbalance,
            TransactionEntries =
            [
                CreateEntry(
                    mainEntry: true,
                    account: account,
                    debitAmount: absoluteAmount,
                    creditAmount: absoluteAmount,
                    currency: currency,
                    options: options,
                    comment: entryComment)
            ]
        };
    }

    private static OaTransactionEntryRequest CreateEntry(
        bool mainEntry,
        string account,
        decimal? debitAmount,
        decimal? creditAmount,
        string? currency,
        BofToOaMappingOptions options,
        string? comment) =>
        new()
        {
            MainEntry = mainEntry,
            Account = account,
            DebitAmount = debitAmount,
            CreditAmount = creditAmount,
            Currency = currency,
            CostCentre = options.DefaultCostCentre,
            CostUnit = options.DefaultCostUnit,
            CashFlow = options.CashFlow,
            Comment = comment
        };

    private static decimal? ResolveAmount(BofExportRecord record)
    {
        if (record.PostedAmount is not null and not 0)
        {
            return record.PostedAmount;
        }

        if (record.TransactionAmount is not null and not 0)
        {
            return record.TransactionAmount;
        }

        if (record.GuestAccountDebit is not null and not 0)
        {
            return record.GuestAccountDebit;
        }

        if (record.GuestAccountCredit is not null and not 0)
        {
            return record.GuestAccountCredit;
        }

        if (record.PackageDebit is not null and not 0)
        {
            return record.PackageDebit;
        }

        if (record.PackageCredit is not null and not 0)
        {
            return record.PackageCredit;
        }

        return record.GrossAmount ?? record.NetAmount;
    }

    private static string? ResolveEntryAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        record.Category switch
        {
            BofTransactionCategory.Payment => ResolvePaymentAccount(record, options),
            BofTransactionCategory.Package => FirstValue(options.PackageLedgerAccount, options.GuestLedgerAccount),
            _ => options.GuestLedgerAccount
        };

    private static string? ResolvePaymentAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        TryGetValue(options.PaymentAccountsByTransactionCode, record.TransactionCode) ??
        TryGetValue(options.PaymentAccountsByMethod, record.PaymentMethod) ??
        options.DefaultPaymentAccount;

    private static DateTime? ResolveTransactionDateTime(BofExportRecord record, BofToOaMappingOptions options)
    {
        var date = options.UseBusinessDate
            ? record.BusinessDate ?? record.TransactionDate ?? record.PostingDate ?? record.InsertDate
            : record.TransactionDate ?? record.BusinessDate ?? record.PostingDate ?? record.InsertDate;
        var time = record.PostingTime ?? record.InsertTime ?? TimeOnly.MinValue;

        return date?.ToDateTime(time);
    }

    private static string? BuildDocumentNumber(BofExportRecord record, BofToOaMappingOptions options)
    {
        if (record.TransactionNumber is null)
        {
            return null;
        }

        return string.Concat(options.DocumentNumberPrefix, record.TransactionNumber.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string? BuildTransactionComment(BofExportRecord record) =>
        FirstValue(record.TransactionDescription, record.Remark, record.Reference);

    private static string? BuildEntryComment(BofExportRecord record, string? transactionComment)
    {
        var debtorName = FirstValue(record.PayeeName, record.GuestName);

        return (debtorName, transactionComment) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{debtorName} - {transactionComment}",
            ({ Length: > 0 }, _) => debtorName,
            (_, { Length: > 0 }) => transactionComment,
            _ => null
        };
    }

    private static string? FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? TryGetValue(IReadOnlyDictionary<string, string> map, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (map.TryGetValue(key, out var value))
        {
            return value;
        }

        return map.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static BofImportWarning CreateWarning(BofExportRecord record, string message) =>
        new(record.SourceFilePath, record.SourceLineNumber, message);
}
