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

        var recordList = records.ToArray();
        var vatAccountsByParentTransactionNumber = CreateVatAccountsByParentTransactionNumber(recordList);
        var transactions = new List<OaTransactionRequest>();
        var warnings = new List<BofImportWarning>();

        foreach (var record in recordList)
        {
            var transaction = ConvertRecord(record, options, vatAccountsByParentTransactionNumber, token, warnings);
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
        IReadOnlyDictionary<long, string> vatAccountsByParentTransactionNumber,
        string? token,
        List<BofImportWarning> warnings)
    {
        if (IsVatRecord(record))
        {
            return null;
        }

        var amount = ResolveAmount(record);
        if (amount is null || amount == 0)
        {
            warnings.Add(CreateWarning(record, "Record does not contain a non-zero amount for OA transaction upload."));
            return null;
        }

        var currency = FirstValue(record.Currency, options.DefaultCurrency);
        var transactionComment = BuildTransactionComment(record);
        var entryComment = BuildEntryComment(record, transactionComment);
        var entries = CreateEntries(record, options, vatAccountsByParentTransactionNumber, amount.Value, currency, entryComment, warnings);
        if (entries.Count == 0)
        {
            return null;
        }

        return new OaTransactionRequest
        {
            Token = token,
            SourceFilePath = record.SourceFilePath,
            SourceLineNumber = record.SourceLineNumber,
            TransactionDate = ResolveTransactionDateTime(record, options),
            TransactionDocumentNumber = BuildDocumentNumber(record, options),
            TransactionComment = transactionComment,
            CorrectDisbalance = options.CorrectDisbalance,
            TransactionEntries = entries
        };
    }

    private static List<OaTransactionEntryRequest> CreateEntries(
        BofExportRecord record,
        BofToOaMappingOptions options,
        IReadOnlyDictionary<long, string> vatAccountsByParentTransactionNumber,
        decimal amount,
        string? currency,
        string? comment,
        List<BofImportWarning> warnings) =>
        record.Category switch
        {
            BofTransactionCategory.Payment => CreatePaymentEntries(record, options, amount, currency, comment, warnings),
            BofTransactionCategory.Charge or BofTransactionCategory.Package => CreateSalesEntries(record, options, vatAccountsByParentTransactionNumber, amount, currency, comment, warnings),
            _ => CreateUnknownCategoryWarning(record, warnings)
        };

    private static List<OaTransactionEntryRequest> CreateSalesEntries(
        BofExportRecord record,
        BofToOaMappingOptions options,
        IReadOnlyDictionary<long, string> vatAccountsByParentTransactionNumber,
        decimal amount,
        string? currency,
        string? comment,
        List<BofImportWarning> warnings)
    {
        var debtorAccount = ResolveDebtorAccount(record, options);
        var revenueAccount = ResolveRevenueAccount(record, options);
        if (string.IsNullOrWhiteSpace(debtorAccount) || string.IsNullOrWhiteSpace(revenueAccount))
        {
            warnings.Add(CreateWarning(
                record,
                $"Missing sales account mapping for category {record.Category}, transaction code '{record.TransactionCode}', subgroup '{record.TransactionSubGroup}'."));
            return [];
        }

        var grossAmount = RoundAmount(Math.Abs(amount));
        var revenueAmount = ResolveRevenueAmount(record, grossAmount);
        var vatAmount = RoundAmount(grossAmount - revenueAmount);
        if (vatAmount < 0)
        {
            vatAmount = 0;
            revenueAmount = grossAmount;
        }

        var vatAccount = ResolveVatAccount(record, options, vatAccountsByParentTransactionNumber);
        if (vatAmount > 0 && string.IsNullOrWhiteSpace(vatAccount))
        {
            warnings.Add(CreateWarning(record, "Missing VAT account mapping for sales transaction with VAT amount."));
            return [];
        }

        return amount >= 0
            ?
            [
                CreateEntry(
                    mainEntry: true,
                    account: debtorAccount,
                    debitAmount: grossAmount,
                    creditAmount: null,
                    currency: currency,
                    options: options,
                    comment: comment),
                CreateEntry(
                    mainEntry: false,
                    account: revenueAccount,
                    debitAmount: null,
                    creditAmount: revenueAmount,
                    currency: currency,
                    options: options,
                    comment: comment),
                .. CreateVatEntries(vatAccount, debitAmount: null, creditAmount: vatAmount, currency, options, comment)
            ]
            :
            [
                CreateEntry(
                    mainEntry: true,
                    account: debtorAccount,
                    debitAmount: null,
                    creditAmount: grossAmount,
                    currency: currency,
                    options: options,
                    comment: comment),
                CreateEntry(
                    mainEntry: false,
                    account: revenueAccount,
                    debitAmount: revenueAmount,
                    creditAmount: null,
                    currency: currency,
                    options: options,
                    comment: comment),
                .. CreateVatEntries(vatAccount, debitAmount: vatAmount, creditAmount: null, currency, options, comment)
            ];
    }

    private static List<OaTransactionEntryRequest> CreatePaymentEntries(
        BofExportRecord record,
        BofToOaMappingOptions options,
        decimal amount,
        string? currency,
        string? comment,
        List<BofImportWarning> warnings)
    {
        var paymentAccount = ResolvePaymentAccount(record, options);
        var debtorAccount = ResolveDebtorAccount(record, options);
        if (string.IsNullOrWhiteSpace(paymentAccount) || string.IsNullOrWhiteSpace(debtorAccount))
        {
            warnings.Add(CreateWarning(
                record,
                $"Missing payment account mapping for transaction code '{record.TransactionCode}', payment method '{record.PaymentMethod}'."));
            return [];
        }

        var absoluteAmount = RoundAmount(Math.Abs(amount));
        return amount >= 0
            ?
            [
                CreateEntry(
                    mainEntry: true,
                    account: paymentAccount,
                    debitAmount: absoluteAmount,
                    creditAmount: null,
                    currency: currency,
                    options: options,
                    comment: comment),
                CreateEntry(
                    mainEntry: false,
                    account: debtorAccount,
                    debitAmount: null,
                    creditAmount: absoluteAmount,
                    currency: currency,
                    options: options,
                    comment: comment)
            ]
            :
            [
                CreateEntry(
                    mainEntry: true,
                    account: paymentAccount,
                    debitAmount: null,
                    creditAmount: absoluteAmount,
                    currency: currency,
                    options: options,
                    comment: comment),
                CreateEntry(
                    mainEntry: false,
                    account: debtorAccount,
                    debitAmount: absoluteAmount,
                    creditAmount: null,
                    currency: currency,
                    options: options,
                    comment: comment)
            ];
    }

    private static List<OaTransactionEntryRequest> CreateVatEntries(
        string? vatAccount,
        decimal? debitAmount,
        decimal? creditAmount,
        string? currency,
        BofToOaMappingOptions options,
        string? comment)
    {
        var amount = debitAmount ?? creditAmount ?? 0m;
        if (amount == 0 || string.IsNullOrWhiteSpace(vatAccount))
        {
            return [];
        }

        return
        [
            CreateEntry(
                mainEntry: false,
                account: vatAccount,
                debitAmount: debitAmount,
                creditAmount: creditAmount,
                currency: currency,
                options: options,
                comment: comment)
        ];
    }

    private static List<OaTransactionEntryRequest> CreateUnknownCategoryWarning(
        BofExportRecord record,
        List<BofImportWarning> warnings)
    {
        warnings.Add(CreateWarning(record, $"Unsupported BOF transaction category '{record.Category}'."));
        return [];
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

    private static string? ResolveDebtorAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        record.Category switch
        {
            BofTransactionCategory.Package => FirstValue(
                record.GuestAccountNumber,
                record.XGuestAccountNumber,
                record.TravelAgentAccountNumber,
                record.CompanyAccountNumber,
                record.SourceAccountNumber,
                record.GroupAccountNumber,
                record.AccountNumber,
                options.PackageLedgerAccount,
                options.GuestLedgerAccount),
            BofTransactionCategory.Payment => FirstValue(
                record.GuestAccountNumber,
                record.XGuestAccountNumber,
                record.TravelAgentAccountNumber,
                record.CompanyAccountNumber,
                record.SourceAccountNumber,
                record.GroupAccountNumber,
                options.GuestLedgerAccount),
            _ => FirstValue(
                record.GuestAccountNumber,
                record.XGuestAccountNumber,
                record.TravelAgentAccountNumber,
                record.CompanyAccountNumber,
                record.SourceAccountNumber,
                record.GroupAccountNumber,
                record.AccountNumber,
                options.GuestLedgerAccount)
        };

    private static string? ResolveRevenueAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        FirstValue(
            record.AccountNumber,
            TryGetValue(options.RevenueAccountsByTransactionCode, record.TransactionCode),
            TryGetValue(options.RevenueAccountsByTransactionSubGroup, record.TransactionSubGroup),
            options.DefaultRevenueAccount);

    private static string? ResolveVatAccount(
        BofExportRecord record,
        BofToOaMappingOptions options,
        IReadOnlyDictionary<long, string> vatAccountsByParentTransactionNumber)
    {
        if (record.TransactionNumber is not null &&
            vatAccountsByParentTransactionNumber.TryGetValue(record.TransactionNumber.Value, out var vatAccount))
        {
            return vatAccount;
        }

        return FirstValue(options.DefaultVatAccount);
    }

    private static IReadOnlyDictionary<long, string> CreateVatAccountsByParentTransactionNumber(
        IReadOnlyCollection<BofExportRecord> records) =>
        records
            .Where(record => IsVatRecord(record) && record.ParentTransactionNumber is not null)
            .Select(record => new
            {
                ParentTransactionNumber = record.ParentTransactionNumber!.Value,
                Account = ResolveCsvEntryAccount(record)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Account))
            .GroupBy(item => item.ParentTransactionNumber)
            .ToDictionary(group => group.Key, group => group.First().Account!, EqualityComparer<long>.Default);

    private static string? ResolveCsvEntryAccount(BofExportRecord record) =>
        FirstValue(
            record.AccountNumber,
            record.GuestAccountNumber,
            record.XGuestAccountNumber,
            record.TravelAgentAccountNumber,
            record.CompanyAccountNumber,
            record.SourceAccountNumber,
            record.GroupAccountNumber);

    private static decimal ResolveRevenueAmount(BofExportRecord record, decimal grossAmount)
    {
        if (record.NetAmount is not null and not 0)
        {
            return RoundAmount(Math.Abs(record.NetAmount.Value));
        }

        return grossAmount;
    }

    private static bool IsVatRecord(BofExportRecord record) =>
        record.Category is BofTransactionCategory.Charge or BofTransactionCategory.Package &&
        (string.Equals(record.TransactionDescription, "VAT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.TransactionSubGroup, "R82", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.RevenueIndicator, "X", StringComparison.OrdinalIgnoreCase));

    private static decimal RoundAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

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

    private static string? ResolvePaymentAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        FirstValue(
            record.AccountNumber,
            TryGetValue(options.PaymentAccountsByTransactionCode, record.TransactionCode),
            TryGetValue(options.PaymentAccountsByMethod, record.PaymentMethod),
            options.DefaultPaymentAccount);

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
