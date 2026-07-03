using System.Net;
using Opera2Oris.ApiClient;
using Opera2Oris.Domain;
using Opera2Oris.Entities;
using Opera2Oris.Middlewear.Outbox;

namespace Opera2Oris.Middlewear;

internal sealed class BofUploadProcessor
{
    private readonly MiddlewearSettings _settings;
    private readonly IBofExportWorker _worker = new BofExportWorker();
    private readonly BofToOaTransactionConverter _converter = new();
    private readonly OaTransactionOutbox? _outbox;
    private readonly ProcessLogger _logger;
    private readonly ProcessedFileArchiver _archiver;
    private readonly OaTransactionPayloadDumper _payloadDumper;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private string? _token;

    public BofUploadProcessor(MiddlewearSettings settings, ProcessLogger logger)
    {
        _settings = settings;
        _logger = logger;
        _archiver = new ProcessedFileArchiver(settings.Archive, logger);
        _payloadDumper = new OaTransactionPayloadDumper(settings.PayloadDump);
        _outbox = settings.Outbox.Enabled ? new OaTransactionOutbox(settings.Outbox.Directory) : null;
    }

    public Task ProcessFolderAsync(CancellationToken cancellationToken = default) =>
        ProcessAsync(searchPattern: null, displayName: _settings.BofExport.SourceDirectory, cancellationToken);

    public Task ProcessFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.Warn($"Skipped missing file: {filePath}");
            return Task.CompletedTask;
        }

        return ProcessAsync(Path.GetFileName(filePath), filePath, cancellationToken);
    }

    private async Task ProcessAsync(string? searchPattern, string displayName, CancellationToken cancellationToken)
    {
        await _processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info($"Processing started: {displayName}");

            var batch = await _worker.ReadAsync(_settings.BofExport.ToImportOptions(searchPattern), cancellationToken).ConfigureAwait(false);
            PrintBatchSummary(batch);

            var conversion = _converter.Convert(batch, _settings.OaWebApi.ToMappingOptions());
            _logger.Info($"OA payloads created: {conversion.Transactions.Count}");
            PrintWarnings("Conversion warnings", conversion.Warnings);
            await DumpPayloadsAsync(conversion, cancellationToken).ConfigureAwait(false);

            if (!_settings.OaWebApi.UploadEnabled)
            {
                _logger.Warn("Upload is disabled.");
                await ArchiveProcessedFilesAsync(batch, conversion, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (conversion.Transactions.Count == 0)
            {
                _logger.Warn("Upload skipped because no OA payloads were created.");
                return;
            }

            if (_outbox is not null)
            {
                var enqueued = await _outbox.EnqueueAsync(conversion.Transactions, cancellationToken).ConfigureAwait(false);
                var summary = await _outbox.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info($"Outbox enqueue: {enqueued} payload(s) accepted, pending: {summary.Pending}, uploaded: {summary.Uploaded}, failed: {summary.Failed}");

                await ArchiveProcessedFilesAsync(batch, conversion, cancellationToken).ConfigureAwait(false);
                await DrainOutboxAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var client = CreateClient();
            var token = await GetTokenAsync(client, cancellationToken).ConfigureAwait(false);
            var uploadResults = await client.UploadTransactionsAsync(conversion.Transactions, token, cancellationToken).ConfigureAwait(false);

            _logger.Info($"Direct upload completed: {uploadResults.Count} transaction(s).");
            await ArchiveProcessedFilesAsync(batch, conversion, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _logger.Info($"Processing finished: {displayName}");
            _processLock.Release();
        }
    }

    public async Task DrainOutboxAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.OaWebApi.UploadEnabled || _outbox is null)
        {
            return;
        }

        await _drainLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ready = await _outbox.GetReadyPendingAsync(_settings.Outbox.MaxBatchSize, cancellationToken).ConfigureAwait(false);
            if (ready.Count == 0)
            {
                return;
            }

            _logger.Info($"Outbox upload started: {ready.Count} ready transaction(s).");

            OaWebApiClient client;
            string token;
            try
            {
                client = CreateClient();
                token = await GetTokenAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
            {
                _logger.Error($"OA API unavailable before upload: {exception.Message}", exception);
                foreach (var record in ready)
                {
                    await _outbox.MarkFailureAsync(record.Id, exception, retryable: true, _settings.Outbox, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            var uploaded = 0;
            for (var index = 0; index < ready.Count; index++)
            {
                var record = ready[index];
                cancellationToken.ThrowIfCancellationRequested();
                record.Payload.Token = token;

                try
                {
                    var result = await client.CreateTransactionAsync(record.Payload, cancellationToken).ConfigureAwait(false);
                    await _outbox.MarkUploadedAsync(record.Id, result, _settings.Outbox.KeepUploadedRecords, cancellationToken).ConfigureAwait(false);
                    uploaded++;
                }
                catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
                {
                    var retryable = IsRetryableUploadFailure(exception);
                    await _outbox.MarkFailureAsync(record.Id, exception, retryable, _settings.Outbox, cancellationToken).ConfigureAwait(false);

                    _logger.Error($"Upload failed for {record.TransactionDocumentNumber ?? record.Id}: {exception.Message}", exception);
                    if (IsConnectivityFailure(exception))
                    {
                        _token = null;
                        foreach (var remainingRecord in ready.Skip(index + 1))
                        {
                            await _outbox.MarkFailureAsync(remainingRecord.Id, exception, retryable: true, _settings.Outbox, cancellationToken).ConfigureAwait(false);
                        }

                        break;
                    }
                }
                finally
                {
                    record.Payload.Token = null;
                }
            }

            var summary = await _outbox.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"Outbox upload finished: uploaded {uploaded}; pending: {summary.Pending}, uploaded total: {summary.Uploaded}, failed: {summary.Failed}");
        }
        finally
        {
            _drainLock.Release();
        }
    }

    public async Task RunOutboxRetryLoopAsync(CancellationToken cancellationToken)
    {
        if (!_settings.OaWebApi.UploadEnabled || _outbox is null)
        {
            _logger.Info("Outbox retry loop is disabled.");
            return;
        }

        _logger.Info($"Outbox retry loop started. Interval: {_settings.Outbox.RetryLoopDelay}.");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainOutboxAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
            {
                _logger.Error($"Outbox retry loop error: {exception.Message}", exception);
            }

            await Task.Delay(_settings.Outbox.RetryLoopDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private OaWebApiClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.OaWebApi.BaseAddress))
        {
            throw new InvalidOperationException("OaWebApi:BaseAddress must be set when upload is enabled.");
        }

        return new OaWebApiClient(new HttpClient
        {
            BaseAddress = new Uri(_settings.OaWebApi.BaseAddress, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.OaWebApi.TimeoutSeconds))
        });
    }

    private async Task<string> GetTokenAsync(IOaWebApiClient client, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_settings.OaWebApi.Token))
        {
            return _settings.OaWebApi.Token;
        }

        if (!string.IsNullOrWhiteSpace(_token))
        {
            return _token;
        }

        var login = _settings.OaWebApi.Login.ToRequest();
        if (string.IsNullOrWhiteSpace(login.User) || string.IsNullOrWhiteSpace(login.Password))
        {
            throw new InvalidOperationException("OaWebApi:Login:User and OaWebApi:Login:Password must be set when upload is enabled and no token is configured.");
        }

        _token = (await client.LogInAsync(login, cancellationToken).ConfigureAwait(false)).Token;
        return _token;
    }

    private static bool IsRetryableUploadFailure(Exception exception) =>
        exception switch
        {
            HttpRequestException => true,
            TimeoutException => true,
            TaskCanceledException => true,
            OaApiException apiException when IsRetryableStatus(apiException.StatusCode) => true,
            _ => false
        };

    private static bool IsConnectivityFailure(Exception exception) =>
        exception switch
        {
            HttpRequestException => true,
            TimeoutException => true,
            TaskCanceledException => true,
            OaApiException apiException when IsConnectivityStatus(apiException.StatusCode) => true,
            _ => false
        };

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static bool IsConnectivityStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private async Task DumpPayloadsAsync(BofToOaConversionResult conversion, CancellationToken cancellationToken)
    {
        if (!_settings.PayloadDump.Enabled)
        {
            return;
        }

        if (conversion.Transactions.Count == 0)
        {
            _logger.Warn("Payload dump skipped because no OA payloads were created.");
            return;
        }

        var dumped = await _payloadDumper.DumpAsync(conversion.Transactions, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Payload dump completed: {dumped} file(s) written to {_settings.PayloadDump.Directory}.");
    }

    private async Task ArchiveProcessedFilesAsync(
        BofExportBatch batch,
        BofToOaConversionResult conversion,
        CancellationToken cancellationToken)
    {
        if (!_settings.Archive.Enabled)
        {
            return;
        }

        if (!_settings.OaWebApi.UploadEnabled && !_settings.Archive.ArchiveWhenUploadDisabled)
        {
            _logger.Warn("Archive skipped because upload is disabled and ArchiveWhenUploadDisabled is false.");
            return;
        }

        var conversionWarningsByFile = conversion.Warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning.FilePath))
            .GroupBy(warning => Path.GetFullPath(warning.FilePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<BofImportWarning>)group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in batch.Files)
        {
            conversionWarningsByFile.TryGetValue(Path.GetFullPath(file.FilePath), out var conversionWarnings);
            try
            {
                await _archiver.ArchiveAsync(file, conversionWarnings ?? Array.Empty<BofImportWarning>(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
            {
                _logger.Error($"Archive failed for '{file.FileName}': {exception.Message}", exception);
            }
        }
    }

    private void PrintBatchSummary(BofExportBatch batch)
    {
        _logger.Info($"Source directory: {batch.SourceDirectory}");
        _logger.Info($"Header dictionary: {batch.HeaderDictionaryPath}");
        _logger.Info($"Files: {batch.Files.Count}; records: {batch.RecordCount}; import warnings: {batch.WarningCount}");

        foreach (var file in batch.Files.OrderBy(file => file.FileName))
        {
            _logger.Info($"{file.FileName}: {file.Category}, {file.Scope}, records: {file.Records.Count}, warnings: {file.Warnings.Count}");
        }

        PrintWarnings("Import warnings", batch.AllWarnings.ToArray());
    }

    private void PrintWarnings(string title, IReadOnlyCollection<BofImportWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        _logger.Warn(title + ":");
        foreach (var warning in warnings.Take(50))
        {
            _logger.Warn(warning.ToString());
        }

        if (warnings.Count > 50)
        {
            _logger.Warn($"{warnings.Count - 50} more warning(s) not shown.");
        }
    }
}
