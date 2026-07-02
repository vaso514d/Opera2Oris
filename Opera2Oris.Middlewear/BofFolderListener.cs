using System.Collections.Concurrent;

namespace Opera2Oris.Middlewear;

internal static class BofFolderListener
{
    public static async Task RunAsync(
        MiddlewearSettings settings,
        BofUploadProcessor processor,
        ProcessLogger logger,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.BofExport.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"BOF export directory was not found: {settings.BofExport.SourceDirectory}");
        }

        var pending = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        using var watcher = new FileSystemWatcher(settings.BofExport.SourceDirectory, settings.BofExport.SearchPattern)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        void Schedule(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || Directory.Exists(filePath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(filePath);
            logger.Info($"File event scheduled: {fullPath}");
            var next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var previous = pending.AddOrUpdate(
                fullPath,
                next,
                (_, existing) =>
                {
                    existing.Cancel();
                    existing.Dispose();
                    return next;
                });

            if (!ReferenceEquals(previous, next))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(settings.Watch.DebounceDelay, next.Token).ConfigureAwait(false);
                    await WaitUntilReadyAsync(fullPath, next.Token).ConfigureAwait(false);
                    logger.Info($"File ready for processing: {fullPath}");
                    await processor.ProcessFileAsync(fullPath, next.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    logger.Error($"Failed to process '{fullPath}': {exception.Message}", exception);
                }
                finally
                {
                    if (pending.TryGetValue(fullPath, out var current) && ReferenceEquals(current, next))
                    {
                        pending.TryRemove(fullPath, out _);
                    }

                    next.Dispose();
                }
            }, CancellationToken.None);
        }

        FileSystemEventHandler onChanged = (_, args) => Schedule(args.FullPath);
        RenamedEventHandler onRenamed = (_, args) => Schedule(args.FullPath);

        watcher.Created += onChanged;
        watcher.Changed += onChanged;
        watcher.Renamed += onRenamed;
        watcher.EnableRaisingEvents = true;

        logger.Info($"Listening folder: {settings.BofExport.SourceDirectory}");
        logger.Info($"Search pattern: {settings.BofExport.SearchPattern}");
        logger.Info("Press Ctrl+C to stop.");

        if (settings.Watch.ProcessExistingOnStart)
        {
            var existingFiles = Directory
                .EnumerateFiles(settings.BofExport.SourceDirectory, settings.BofExport.SearchPattern)
                .OrderBy(file => file)
                .ToArray();

            logger.Info($"Existing file scan scheduled: {existingFiles.Length} file(s), delay: {settings.Watch.DebounceDelay}.");
            foreach (var file in existingFiles)
            {
                Schedule(file);
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var source in pending.Values)
            {
                source.Cancel();
                source.Dispose();
            }
        }
    }

    private static async Task WaitUntilReadyAsync(string filePath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 0)
                {
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }
}
