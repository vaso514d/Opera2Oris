using Opera2Oris.Middlewear;

ProcessLogger? logger = null;

try
{
    var commandLine = RunnerCommandLine.Parse(args);
    if (commandLine.ShowHelp)
    {
        PrintHelp();
        return 0;
    }

    var configPath = commandLine.ConfigPath ?? FindDefaultConfigPath();
    var settings = MiddlewearSettings.Load(configPath);
    logger = new ProcessLogger(settings.Logging);
    logger.Info($"Configuration loaded: {configPath}");

    var watchEnabled = commandLine.WatchEnabledOverride ?? settings.Watch.Enabled;
    var processor = new BofUploadProcessor(settings, logger);

    using var stopping = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        logger.Warn("Stop requested by Ctrl+C.");
        stopping.Cancel();
    };

    if (settings.Outbox.ProcessPendingOnStart)
    {
        await processor.DrainOutboxAsync(stopping.Token).ConfigureAwait(false);
    }

    if (!watchEnabled)
    {
        await DelayBeforeInitialScanAsync(settings, logger, stopping.Token).ConfigureAwait(false);
        await processor.ProcessFolderAsync(stopping.Token).ConfigureAwait(false);
    }

    if (watchEnabled && !stopping.IsCancellationRequested)
    {
        await Task.WhenAll(
            BofFolderListener.RunAsync(settings, processor, logger, stopping.Token),
            processor.RunOutboxRetryLoopAsync(stopping.Token)).ConfigureAwait(false);
    }

    logger.Info("Process stopped.");
    return 0;
}
catch (OperationCanceledException)
{
    logger?.Info("Process canceled.");
    return 0;
}
catch (Exception exception)
{
    if (logger is null)
    {
        Console.Error.WriteLine(exception.Message);
        Console.Error.WriteLine(exception);
    }
    else
    {
        logger.Error($"Process failed: {exception.Message}", exception);
    }

    return 1;
}
finally
{
    logger?.Dispose();
}

static string FindDefaultConfigPath()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
        Path.Combine(AppContext.BaseDirectory, "appsettings.json")
    };

    var path = candidates.FirstOrDefault(File.Exists);
    if (path is null)
    {
        throw new FileNotFoundException("appsettings.json was not found. Use --config <path>.");
    }

    return path;
}

static void PrintHelp()
{
    Console.WriteLine("Opera2Oris.Middlewear");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  Opera2Oris.Middlewear [--config <appsettings.json>] [--once|--watch]");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  BofExport:SourceDirectory       Folder to listen/read.");
    Console.WriteLine("  BofExport:HeaderDictionaryPath  Headers.txt path.");
    Console.WriteLine("  Logging:*                       Console/file logging settings.");
    Console.WriteLine("  Watch:Enabled                   Keep listening for new/changed CSV files.");
    Console.WriteLine("  Archive:*                       Compressed archive settings for processed CSV files.");
    Console.WriteLine("  Outbox:*                        Durable pending-upload queue and retry settings.");
    Console.WriteLine("  PayloadDump:*                   Optional final OA transaction JSON dump.");
    Console.WriteLine("  OaWebApi:*                      API base URL, login/token, upload flag, account mapping.");
}

static async Task DelayBeforeInitialScanAsync(
    MiddlewearSettings settings,
    ProcessLogger logger,
    CancellationToken cancellationToken)
{
    logger.Info($"Initial file scan delay: {settings.Watch.DebounceDelay}.");
    await Task.Delay(settings.Watch.DebounceDelay, cancellationToken).ConfigureAwait(false);
}

internal sealed record RunnerCommandLine(string? ConfigPath, bool? WatchEnabledOverride, bool ShowHelp)
{
    public static RunnerCommandLine Parse(string[] args)
    {
        string? configPath = null;
        bool? watchEnabled = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase))
            {
                return new RunnerCommandLine(null, null, true);
            }

            if (string.Equals(argument, "--config", StringComparison.OrdinalIgnoreCase))
            {
                configPath = RequireValue(args, ref index, "--config");
                continue;
            }

            if (string.Equals(argument, "--once", StringComparison.OrdinalIgnoreCase))
            {
                watchEnabled = false;
                continue;
            }

            if (string.Equals(argument, "--watch", StringComparison.OrdinalIgnoreCase))
            {
                watchEnabled = true;
                continue;
            }

            throw new ArgumentException($"Unexpected argument: {argument}");
        }

        return new RunnerCommandLine(configPath, watchEnabled, false);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
}
