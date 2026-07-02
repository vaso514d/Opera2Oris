using Microsoft.Extensions.Hosting;

namespace Opera2Oris.Middlewear;

internal sealed class MiddlewearHostedService : BackgroundService
{
    private readonly MiddlewearSettings _settings;
    private readonly ProcessLogger _logger;
    private readonly BofUploadProcessor _processor;

    public MiddlewearHostedService(MiddlewearSettings settings, ProcessLogger logger)
    {
        _settings = settings;
        _logger = logger;
        _processor = new BofUploadProcessor(settings, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Hosted watch worker started.");

        if (_settings.Outbox.ProcessPendingOnStart)
        {
            await _processor.DrainOutboxAsync(stoppingToken).ConfigureAwait(false);
        }

        await Task.WhenAll(
            BofFolderListener.RunAsync(_settings, _processor, _logger, stoppingToken),
            _processor.RunOutboxRetryLoopAsync(stoppingToken)).ConfigureAwait(false);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Hosted watch worker stopping.");
        return base.StopAsync(cancellationToken);
    }
}
