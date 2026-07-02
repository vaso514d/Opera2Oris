using System.Text.Json;
using Opera2Oris.Domain;
using Opera2Oris.Entities;

namespace Opera2Oris.Middlewear;

internal sealed class MiddlewearSettings
{
    public BofExportSettings BofExport { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public WatchSettings Watch { get; set; } = new();

    public ArchiveSettings Archive { get; set; } = new();

    public OutboxSettings Outbox { get; set; } = new();

    public PayloadDumpSettings PayloadDump { get; set; } = new();

    public OaWebApiSettings OaWebApi { get; set; } = new();

    public static MiddlewearSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Configuration file was not found.", path);
        }

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var settings = JsonSerializer.Deserialize<MiddlewearSettings>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Configuration file is empty: {path}");
        settings.Normalize(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());

        return settings;
    }

    private void Normalize(string configDirectory)
    {
        BofExport.SourceDirectory = ResolvePath(BofExport.SourceDirectory, configDirectory);
        if (string.IsNullOrWhiteSpace(BofExport.HeaderDictionaryPath))
        {
            BofExport.HeaderDictionaryPath = Path.Combine(configDirectory, "Headers.txt");
        }
        else
        {
            BofExport.HeaderDictionaryPath = ResolvePath(BofExport.HeaderDictionaryPath, configDirectory);
        }

        Logging.Directory = ResolvePath(Logging.Directory, configDirectory);
        Archive.Directory = ResolvePath(Archive.Directory, BofExport.SourceDirectory);
        Outbox.Directory = ResolvePath(Outbox.Directory, configDirectory);
        PayloadDump.Directory = ResolvePath(PayloadDump.Directory, BofExport.SourceDirectory);
    }

    private static string ResolvePath(string? path, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return configDirectory;
        }

        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(configDirectory, path));
    }
}

internal sealed class BofExportSettings
{
    public string SourceDirectory { get; set; } = Directory.GetCurrentDirectory();

    public string HeaderDictionaryPath { get; set; } = string.Empty;

    public string SearchPattern { get; set; } = "*.csv";

    public string Delimiter { get; set; } = ";";

    public string EncodingName { get; set; } = "windows-1251";

    public BofExportImportOptions ToImportOptions(string? searchPattern = null)
    {
        if (Delimiter.Length != 1)
        {
            throw new InvalidOperationException("BofExport:Delimiter must be exactly one character.");
        }

        return new BofExportImportOptions(SourceDirectory, HeaderDictionaryPath)
        {
            SearchPattern = string.IsNullOrWhiteSpace(searchPattern) ? SearchPattern : searchPattern,
            Delimiter = Delimiter[0],
            EncodingName = EncodingName
        };
    }
}

internal sealed class WatchSettings
{
    public bool Enabled { get; set; } = true;

    public bool ProcessExistingOnStart { get; set; } = true;

    public int DebounceSeconds { get; set; } = 5;

    public TimeSpan DebounceDelay => TimeSpan.FromSeconds(Math.Max(1, DebounceSeconds));
}

internal sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "logs";

    public string FilePrefix { get; set; } = "opera2oris";
}

internal sealed class ArchiveSettings
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "archive";

    public bool DeleteSourceAfterArchive { get; set; } = true;

    public bool UseDateSubfolders { get; set; } = true;

    public bool IncludeTimestampInArchiveName { get; set; } = true;

    public bool ArchiveOnlyWithoutWarnings { get; set; } = true;

    public bool ArchiveWhenUploadDisabled { get; set; }
}

internal sealed class OutboxSettings
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "outbox";

    public bool ProcessPendingOnStart { get; set; } = true;

    public int RetryLoopSeconds { get; set; } = 30;

    public int RetryBaseDelaySeconds { get; set; } = 30;

    public int RetryMaxDelaySeconds { get; set; } = 1800;

    public int MaxBatchSize { get; set; } = 100;

    public bool KeepUploadedRecords { get; set; } = true;

    public TimeSpan RetryLoopDelay => TimeSpan.FromSeconds(Math.Max(5, RetryLoopSeconds));
}

internal sealed class PayloadDumpSettings
{
    public bool Enabled { get; set; }

    public string Directory { get; set; } = "payload-dump";
}

internal sealed class OaWebApiSettings
{
    public string BaseAddress { get; set; } = string.Empty;

    public bool UploadEnabled { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public string? Token { get; set; }

    public OaLoginSettings Login { get; set; } = new();

    public OaMappingSettings Mapping { get; set; } = new();

    public BofToOaMappingOptions ToMappingOptions() => Mapping.ToOptions();
}

internal sealed class OaLoginSettings
{
    public string? User { get; set; }

    public string? Password { get; set; }

    public string? Language { get; set; }

    public string? DatabaseName { get; set; }

    public string? DatabaseUserName { get; set; }

    public string? DatabaseUserPassword { get; set; }

    public bool? DatabaseIsLocal { get; set; }

    public string? LocalDatabasePath { get; set; }

    public string? SqlServer { get; set; }

    public bool? UseWindowsAuthentication { get; set; }

    public OaLoginRequest ToRequest() =>
        new()
        {
            User = EmptyToNull(User),
            Password = EmptyToNull(Password),
            Language = EmptyToNull(Language),
            DatabaseName = EmptyToNull(DatabaseName),
            DatabaseUserName = EmptyToNull(DatabaseUserName),
            DatabaseUserPassword = EmptyToNull(DatabaseUserPassword),
            DatabaseIsLocal = DatabaseIsLocal,
            LocalDatabasePath = EmptyToNull(LocalDatabasePath),
            SqlServer = EmptyToNull(SqlServer),
            UseWindowsAuthentication = UseWindowsAuthentication
        };

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal sealed class OaMappingSettings
{
    public string? GuestLedgerAccount { get; set; }

    public string? PackageLedgerAccount { get; set; }

    public string? DefaultRevenueAccount { get; set; }

    public string? DefaultVatAccount { get; set; }

    public string? DefaultPaymentAccount { get; set; }

    public string? DefaultCurrency { get; set; } = "GEL";

    public string? DefaultCostCentre { get; set; }

    public string? DefaultCostUnit { get; set; }

    public string? CashFlow { get; set; }

    public string DocumentNumberPrefix { get; set; } = "OPERA-";

    public bool UseBusinessDate { get; set; } = true;

    public bool? CorrectDisbalance { get; set; } = false;

    public Dictionary<string, string> RevenueAccountsByTransactionCode { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RevenueAccountsByTransactionSubGroup { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PaymentAccountsByTransactionCode { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PaymentAccountsByMethod { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public BofToOaMappingOptions ToOptions() =>
        new()
        {
            GuestLedgerAccount = EmptyToNull(GuestLedgerAccount),
            PackageLedgerAccount = EmptyToNull(PackageLedgerAccount),
            DefaultRevenueAccount = EmptyToNull(DefaultRevenueAccount),
            DefaultVatAccount = EmptyToNull(DefaultVatAccount),
            DefaultPaymentAccount = EmptyToNull(DefaultPaymentAccount),
            DefaultCurrency = EmptyToNull(DefaultCurrency),
            DefaultCostCentre = EmptyToNull(DefaultCostCentre),
            DefaultCostUnit = EmptyToNull(DefaultCostUnit),
            CashFlow = EmptyToNull(CashFlow),
            DocumentNumberPrefix = DocumentNumberPrefix,
            UseBusinessDate = UseBusinessDate,
            CorrectDisbalance = CorrectDisbalance,
            RevenueAccountsByTransactionCode = CleanMap(RevenueAccountsByTransactionCode),
            RevenueAccountsByTransactionSubGroup = CleanMap(RevenueAccountsByTransactionSubGroup),
            PaymentAccountsByTransactionCode = CleanMap(PaymentAccountsByTransactionCode),
            PaymentAccountsByMethod = CleanMap(PaymentAccountsByMethod)
        };

    private static IReadOnlyDictionary<string, string> CleanMap(Dictionary<string, string> map) =>
        map
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
