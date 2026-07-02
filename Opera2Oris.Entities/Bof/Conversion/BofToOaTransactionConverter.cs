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
        var relatedSalesRecordsByParentTransactionNumber = CreateRelatedSalesRecordsByParentTransactionNumber(recordList);
        var duplicatePaymentRecords = CreateDuplicatePaymentRecords(recordList);
        var transactions = new List<OaTransactionRequest>();
        var warnings = new List<BofImportWarning>();

        foreach (var record in recordList)
        {
            var transaction = ConvertRecord(record, options, relatedSalesRecordsByParentTransactionNumber, duplicatePaymentRecords, token, warnings);
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
        IReadOnlyDictionary<long, IReadOnlyList<BofExportRecord>> relatedSalesRecordsByParentTransactionNumber,
        IReadOnlySet<BofExportRecord> duplicatePaymentRecords,
        string? token,
        List<BofImportWarning> warnings)
    {
        if (duplicatePaymentRecords.Contains(record))
        {
            return null;
        }

        if (IsRelatedSalesRecord(record))
        {
            return null;
        }

        var amount = ResolveAmount(record);
        if (amount is null || amount == 0)
        {
            warnings.Add(CreateWarning(record, "Record does not contain a non-zero amount for OA transaction upload."));
            return null;
        }

        var transactionComment = BuildTransactionComment(record);
        var entries = CreateEntries(record, options, relatedSalesRecordsByParentTransactionNumber, amount.Value, warnings);
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
        IReadOnlyDictionary<long, IReadOnlyList<BofExportRecord>> relatedSalesRecordsByParentTransactionNumber,
        decimal amount,
        List<BofImportWarning> warnings) =>
        record.Category switch
        {
            BofTransactionCategory.Payment => CreatePaymentEntries(record, options, amount, warnings),
            BofTransactionCategory.Charge or BofTransactionCategory.Package => CreateSalesEntries(record, options, relatedSalesRecordsByParentTransactionNumber, amount, warnings),
            _ => CreateUnknownCategoryWarning(record, warnings)
        };

    private static List<OaTransactionEntryRequest> CreateSalesEntries(
        BofExportRecord record,
        BofToOaMappingOptions options,
        IReadOnlyDictionary<long, IReadOnlyList<BofExportRecord>> relatedSalesRecordsByParentTransactionNumber,
        decimal amount,
        List<BofImportWarning> warnings)
    {
        var debtorAccount = ResolveDebtorAccount(record, options);
        if (string.IsNullOrWhiteSpace(debtorAccount))
        {
            warnings.Add(CreateWarning(
                record,
                $"Missing debtor account mapping for category {record.Category}, transaction code '{record.TransactionCode}', subgroup '{record.TransactionSubGroup}'."));
            return [];
        }

        var debtorAmount = RoundAmount(Math.Abs(record.GrossAmount ?? amount));
        var relatedRecords = GetRelatedSalesRecords(record, relatedSalesRecordsByParentTransactionNumber);
        var isPositiveAmount = amount >= 0;
        var entries = new List<OaTransactionEntryRequest>
        {
            CreateEntry(
                mainEntry: true,
                account: debtorAccount,
                debitAmount: isPositiveAmount ? debtorAmount : null,
                creditAmount: isPositiveAmount ? null : debtorAmount,
                sourceRecord: record,
                options: options)
        };

        var creditEntries = new List<OaTransactionEntryRequest>();
        var creditTotal = 0m;
        var creditRows = new[] { record }.Concat(relatedRecords);
        foreach (var creditRow in creditRows)
        {
            var creditAccount = ResolveSalesCreditAccount(creditRow, options);
            if (string.IsNullOrWhiteSpace(creditAccount))
            {
                warnings.Add(CreateWarning(
                    creditRow,
                    $"Missing sales credit account mapping for transaction code '{creditRow.TransactionCode}', subgroup '{creditRow.TransactionSubGroup}'."));
                return [];
            }

            var fallbackAmount = ReferenceEquals(record, creditRow) && relatedRecords.Count == 0
                ? debtorAmount
                : (decimal?)null;
            var creditAmount = ResolveSalesCreditAmount(creditRow, fallbackAmount);
            if (creditAmount is null || creditAmount == 0)
            {
                warnings.Add(CreateWarning(creditRow, "Sales credit row does not contain a non-zero net_amount."));
                return [];
            }

            var absoluteCreditAmount = RoundAmount(Math.Abs(creditAmount.Value));
            creditTotal += absoluteCreditAmount;
            creditEntries.Add(CreateEntry(
                mainEntry: false,
                account: creditAccount,
                debitAmount: isPositiveAmount ? null : absoluteCreditAmount,
                creditAmount: isPositiveAmount ? absoluteCreditAmount : null,
                sourceRecord: creditRow,
                options: options,
                fallbackCurrency: record.Currency));
        }

        var difference = RoundAmount(debtorAmount - creditTotal);
        if (difference != 0)
        {
            warnings.Add(CreateWarning(
                record,
                $"Sales transaction chain is not balanced: gross amount {debtorAmount.ToString(CultureInfo.InvariantCulture)} does not match net_amount sum {creditTotal.ToString(CultureInfo.InvariantCulture)}."));
            return [];
        }

        entries.AddRange(creditEntries);
        return entries;
    }

    private static List<OaTransactionEntryRequest> CreatePaymentEntries(
        BofExportRecord record,
        BofToOaMappingOptions options,
        decimal amount,
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
                    sourceRecord: record,
                    options: options),
                CreateEntry(
                    mainEntry: false,
                    account: debtorAccount,
                    debitAmount: null,
                    creditAmount: absoluteAmount,
                    sourceRecord: record,
                    options: options)
            ]
            :
            [
                CreateEntry(
                    mainEntry: true,
                    account: paymentAccount,
                    debitAmount: null,
                    creditAmount: absoluteAmount,
                    sourceRecord: record,
                    options: options),
                CreateEntry(
                    mainEntry: false,
                    account: debtorAccount,
                    debitAmount: absoluteAmount,
                    creditAmount: null,
                    sourceRecord: record,
                    options: options)
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
        BofExportRecord sourceRecord,
        BofToOaMappingOptions options,
        string? fallbackCurrency = null) =>
        new()
        {
            MainEntry = mainEntry,
            Account = account,
            DebitAmount = debitAmount,
            CreditAmount = creditAmount,
            Currency = FirstValue(sourceRecord.Currency, fallbackCurrency, options.DefaultCurrency),
            CostCentre = options.DefaultCostCentre,
            CostUnit = options.DefaultCostUnit,
            CashFlow = options.CashFlow,
            Comment = BuildEntryComment(sourceRecord)
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

    private static string? ResolveSalesCreditAccount(BofExportRecord record, BofToOaMappingOptions options) =>
        IsVatRecord(record)
            ? FirstValue(ResolveCsvEntryAccount(record), options.DefaultVatAccount)
            : ResolveRevenueAccount(record, options);

    private static IReadOnlyDictionary<long, IReadOnlyList<BofExportRecord>> CreateRelatedSalesRecordsByParentTransactionNumber(
        IReadOnlyCollection<BofExportRecord> records) =>
        records
            .Where(IsRelatedSalesRecord)
            .GroupBy(record => record.ParentTransactionNumber!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BofExportRecord>)group
                    .OrderBy(record => record.SourceLineNumber)
                    .ToArray(),
                EqualityComparer<long>.Default);

    private static IReadOnlyList<BofExportRecord> GetRelatedSalesRecords(
        BofExportRecord record,
        IReadOnlyDictionary<long, IReadOnlyList<BofExportRecord>> relatedSalesRecordsByParentTransactionNumber)
    {
        if (record.TransactionNumber is null)
        {
            return [];
        }

        return relatedSalesRecordsByParentTransactionNumber.TryGetValue(record.TransactionNumber.Value, out var records)
            ? records
            : [];
    }

    private static string? ResolveCsvEntryAccount(BofExportRecord record) =>
        FirstValue(
            record.AccountNumber,
            record.GuestAccountNumber,
            record.XGuestAccountNumber,
            record.TravelAgentAccountNumber,
            record.CompanyAccountNumber,
            record.SourceAccountNumber,
            record.GroupAccountNumber);

    private static decimal? ResolveSalesCreditAmount(BofExportRecord record, decimal? fallbackAmount) =>
        record.NetAmount is not null and not 0
            ? record.NetAmount.Value
            : fallbackAmount;

    private static bool IsVatRecord(BofExportRecord record) =>
        record.Category is BofTransactionCategory.Charge or BofTransactionCategory.Package &&
        (string.Equals(record.TransactionDescription, "VAT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.TransactionSubGroup, "R82", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.RevenueIndicator, "X", StringComparison.OrdinalIgnoreCase));

    private static bool IsRelatedSalesRecord(BofExportRecord record) =>
        record.Category is BofTransactionCategory.Charge or BofTransactionCategory.Package &&
        record.ParentTransactionNumber is not null;

    private static IReadOnlySet<BofExportRecord> CreateDuplicatePaymentRecords(
        IReadOnlyCollection<BofExportRecord> records) =>
        records
            .Where(IsPaymentRecordWithTransactionNumber)
            .GroupBy(record => record.TransactionNumber!.Value)
            .SelectMany(group => group
                .OrderBy(GetPaymentScopePriority)
                .ThenBy(record => record.SourceFilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.SourceLineNumber)
                .Skip(1))
            .ToHashSet();

    private static bool IsPaymentRecordWithTransactionNumber(BofExportRecord record) =>
        record.Category == BofTransactionCategory.Payment &&
        record.TransactionNumber is not null;

    private static int GetPaymentScopePriority(BofExportRecord record) =>
        record.Scope switch
        {
            BofExportScope.Daily => 0,
            BofExportScope.CheckOut => 1,
            BofExportScope.UntilToday => 2,
            _ => 3
        };

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

    private static string? BuildEntryComment(BofExportRecord record) =>
        BuildTransactionComment(record);

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
