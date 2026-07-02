using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opera2Oris.Entities;
using Opera2Oris.Middlewear;

namespace Opera2Oris.Middlewear.Outbox;

internal sealed class OaTransactionOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _recordsDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OaTransactionOutbox(string outboxDirectory)
    {
        Directory.CreateDirectory(outboxDirectory);
        _recordsDirectory = Path.Combine(outboxDirectory, "records");
        Directory.CreateDirectory(_recordsDirectory);
    }

    public async Task<int> EnqueueAsync(IEnumerable<OaTransactionRequest> transactions, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var transaction in transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await EnqueueAsync(transaction, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    public async Task<bool> EnqueueAsync(OaTransactionRequest transaction, CancellationToken cancellationToken)
    {
        var id = CreateStableId(transaction);
        var path = GetRecordPath(id);
        var now = DateTime.UtcNow;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadRecordIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing?.Status == OaTransactionOutboxStatus.Uploaded)
            {
                return false;
            }

            transaction.Token = null;
            var record = existing ?? new OaTransactionOutboxRecord
            {
                Id = id,
                CreatedAtUtc = now
            };

            record.Status = OaTransactionOutboxStatus.Pending;
            record.SourceFilePath = transaction.SourceFilePath;
            record.SourceLineNumber = transaction.SourceLineNumber;
            record.TransactionDocumentNumber = transaction.TransactionDocumentNumber;
            record.Payload = transaction;
            record.UpdatedAtUtc = now;
            record.NextAttemptAtUtc ??= now;

            await WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<OaTransactionOutboxRecord>> GetReadyPendingAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var records = new List<OaTransactionOutboxRecord>();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_recordsDirectory, "*.json").OrderBy(File.GetCreationTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = await ReadRecordIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
                if (record is null ||
                    record.Status != OaTransactionOutboxStatus.Pending ||
                    record.NextAttemptAtUtc > now)
                {
                    continue;
                }

                records.Add(record);
                if (records.Count >= maxCount)
                {
                    break;
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return records;
    }

    public async Task MarkUploadedAsync(
        string id,
        OaMutationResult result,
        bool keepUploadedRecords,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetRecordPath(id);
            var record = await ReadRecordIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return;
            }

            if (!keepUploadedRecords)
            {
                File.Delete(path);
                return;
            }

            record.Status = OaTransactionOutboxStatus.Uploaded;
            record.OaTransactionsId = result.GetInt64Id();
            record.LastError = null;
            record.LastErrorType = null;
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.NextAttemptAtUtc = null;
            record.Payload.Token = null;

            await WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkFailureAsync(
        string id,
        Exception exception,
        bool retryable,
        OutboxSettings settings,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await ReadRecordIfExistsAsync(GetRecordPath(id), cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return;
            }

            record.AttemptCount++;
            record.Status = retryable ? OaTransactionOutboxStatus.Pending : OaTransactionOutboxStatus.Failed;
            record.LastError = exception.Message;
            record.LastErrorType = exception.GetType().Name;
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.NextAttemptAtUtc = retryable ? record.UpdatedAtUtc.Add(CalculateDelay(record.AttemptCount, settings)) : null;
            record.Payload.Token = null;

            await WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<OutboxSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var pending = 0;
        var uploaded = 0;
        var failed = 0;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_recordsDirectory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = await ReadRecordIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
                switch (record?.Status)
                {
                    case OaTransactionOutboxStatus.Pending:
                        pending++;
                        break;
                    case OaTransactionOutboxStatus.Uploaded:
                        uploaded++;
                        break;
                    case OaTransactionOutboxStatus.Failed:
                        failed++;
                        break;
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return new OutboxSummary(pending, uploaded, failed);
    }

    private static string CreateStableId(OaTransactionRequest transaction)
    {
        var key = !string.IsNullOrWhiteSpace(transaction.TransactionDocumentNumber)
            ? transaction.TransactionDocumentNumber
            : $"{transaction.SourceFilePath}|{transaction.SourceLineNumber}";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key ?? Guid.NewGuid().ToString("N")));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetRecordPath(string id) => Path.Combine(_recordsDirectory, $"{id}.json");

    private static TimeSpan CalculateDelay(int attemptCount, OutboxSettings settings)
    {
        var baseSeconds = Math.Max(1, settings.RetryBaseDelaySeconds);
        var maxSeconds = Math.Max(baseSeconds, settings.RetryMaxDelaySeconds);
        var exponential = baseSeconds * Math.Pow(2, Math.Min(8, Math.Max(0, attemptCount - 1)));
        return TimeSpan.FromSeconds(Math.Min(maxSeconds, exponential));
    }

    private static async Task<OaTransactionOutboxRecord?> ReadRecordIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OaTransactionOutboxRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteRecordAsync(OaTransactionOutboxRecord record, CancellationToken cancellationToken)
    {
        var path = GetRecordPath(record.Id);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}

internal sealed record OutboxSummary(int Pending, int Uploaded, int Failed);
