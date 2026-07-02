namespace Opera2Oris.Entities;

public sealed class BofExportRecord
{
    private readonly IReadOnlyDictionary<int, BofFieldValue> _fieldsByOrdinal;
    private readonly IReadOnlyDictionary<string, BofFieldValue> _fieldsByKey;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BofFieldValue>> _fieldsByName;

    public BofExportRecord(
        string sourceFilePath,
        long sourceLineNumber,
        BofTransactionCategory category,
        BofExportScope scope,
        IReadOnlyList<BofFieldValue> fields)
    {
        SourceFilePath = sourceFilePath;
        SourceLineNumber = sourceLineNumber;
        Category = category;
        Scope = scope;
        Fields = fields;

        _fieldsByOrdinal = fields.ToDictionary(field => field.Column.Ordinal);
        _fieldsByKey = fields
            .GroupBy(field => field.Column.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _fieldsByName = fields
            .GroupBy(field => field.Column.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<BofFieldValue>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public string SourceFilePath { get; }

    public string SourceFileName => System.IO.Path.GetFileName(SourceFilePath);

    public long SourceLineNumber { get; }

    public BofTransactionCategory Category { get; }

    public BofExportScope Scope { get; }

    public IReadOnlyList<BofFieldValue> Fields { get; }

    public string? Resort => GetString(1);

    public long? TransactionNumber => GetInt64(2);

    public long? ParentTransactionNumber => GetInt64(3);

    public long? TransactionActionId => GetInt64(4);

    public string? TransactionCode => GetString(5);

    public string? TransactionDescription => GetString(6);

    public DateOnly? InsertDate => GetDate(7);

    public DateOnly? BusinessDate => GetDate(8);

    public TimeOnly? InsertTime => GetTime(9);

    public decimal? TransactionAmount => GetDecimal(10);

    public decimal? PricePerUnit => GetDecimal(11);

    public decimal? Quantity => GetDecimal(12);

    public string? Currency => GetString(13);

    public decimal? ExchangeRate => GetDecimal(14);

    public long? FolioNumber => GetInt64(39);

    public string? AccountNumber => GetString(19);

    public string? PayeeName => GetString(20);

    public string? NameType => GetString(21);

    public long? NameId => GetInt64(22);

    public string? GuestAccountNumber => GetString(23);

    public string? GuestName => GetString(24);

    public string? Room => GetString(33);

    public string? TravelAgentAccountNumber => GetString(42);

    public string? CompanyAccountNumber => GetString(45);

    public string? SourceAccountNumber => GetString(48);

    public string? GroupAccountNumber => GetString(51);

    public string? XGuestAccountNumber => GetString(54);

    public string? TransactionMainGroup => GetString(58);

    public string? TransactionSubGroup => GetString(59);

    public decimal? GrossAmount => GetDecimal(60);

    public decimal? NetAmount => GetDecimal(61);

    public decimal? GuestAccountCredit => GetDecimal(62);

    public decimal? GuestAccountDebit => GetDecimal(63);

    public decimal? PackageCredit => GetDecimal(64);

    public decimal? PackageDebit => GetDecimal(65);

    public string? TransactionType => GetString(69);

    public string? RevenueIndicator => GetString(70);

    public string? StatisticalTransactionType => GetString(71);

    public DateOnly? TransactionDate => GetDate(79);

    public DateOnly? PostingDate => GetDate(80);

    public TimeOnly? PostingTime => GetTime(81);

    public string? Remark => GetString(83);

    public string? Reference => GetString(84);

    public string? PaymentMethod => GetString(92);

    public long? ArticleId => GetInt64(111);

    public string? ArticleCode => GetString(112);

    public decimal? PostedAmount => GetDecimal(113);

    public decimal? PostedExchangeRate => GetDecimal(114);

    public DateOnly? SystemDate => GetDate(117);

    public TimeOnly? SystemTime => GetTime(118);

    public BofFieldValue? GetField(int ordinal) =>
        _fieldsByOrdinal.TryGetValue(ordinal, out var field) ? field : null;

    public BofFieldValue? GetFieldByKey(string key) =>
        _fieldsByKey.TryGetValue(key, out var field) ? field : null;

    public IReadOnlyList<BofFieldValue> FindFields(string headerName) =>
        _fieldsByName.TryGetValue(headerName, out var fields) ? fields : [];

    public string? GetString(int ordinal) => GetField(ordinal)?.AsString();

    public long? GetInt64(int ordinal) => GetField(ordinal)?.AsInt64();

    public decimal? GetDecimal(int ordinal) => GetField(ordinal)?.AsDecimal();

    public DateOnly? GetDate(int ordinal) => GetField(ordinal)?.AsDate();

    public TimeOnly? GetTime(int ordinal) => GetField(ordinal)?.AsTime();

    public DateTime? GetDateTime(int ordinal) => GetField(ordinal)?.AsDateTime();
}
