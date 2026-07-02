using System.IO.Compression;
using Opera2Oris.Entities;

namespace Opera2Oris.Middlewear;

internal sealed class ProcessedFileArchiver
{
    private readonly ArchiveSettings _settings;
    private readonly ProcessLogger _logger;

    public ProcessedFileArchiver(ArchiveSettings settings, ProcessLogger logger)
    {
        _settings = settings;
        _logger = logger;
        if (_settings.Enabled)
        {
            Directory.CreateDirectory(_settings.Directory);
        }
    }

    public async Task ArchiveAsync(
        BofExportFile file,
        IReadOnlyCollection<BofImportWarning> conversionWarnings,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        if (!File.Exists(file.FilePath))
        {
            _logger.Warn($"Archive skipped because source file is missing: {file.FilePath}");
            return;
        }

        if (_settings.ArchiveOnlyWithoutWarnings && (file.Warnings.Count > 0 || conversionWarnings.Count > 0))
        {
            _logger.Warn($"Archive skipped because file has warnings: {file.FileName}");
            return;
        }

        var archiveDirectory = _settings.UseDateSubfolders
            ? Path.Combine(_settings.Directory, DateTime.Now.ToString("yyyyMMdd"))
            : _settings.Directory;
        Directory.CreateDirectory(archiveDirectory);

        var archivePath = BuildArchivePath(file.FilePath, archiveDirectory);
        var tempPath = $"{archivePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await WaitUntilReadableAsync(file.FilePath, cancellationToken).ConfigureAwait(false);

            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(file.FilePath, Path.GetFileName(file.FilePath), CompressionLevel.Optimal);
            }

            File.Move(tempPath, archivePath, overwrite: false);
            _logger.Info($"Archived processed file '{file.FileName}' to '{archivePath}'.");

            if (_settings.DeleteSourceAfterArchive)
            {
                File.Delete(file.FilePath);
                _logger.Info($"Deleted processed source file after archive: {file.FilePath}");
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private string BuildArchivePath(string sourceFilePath, string archiveDirectory)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var extension = Path.GetExtension(sourceFilePath);
        var timestamp = _settings.IncludeTimestampInArchiveName
            ? "." + DateTime.Now.ToString("yyyyMMddHHmmssfff")
            : string.Empty;

        var archiveName = $"{fileName}{timestamp}{extension}.zip";
        var archivePath = Path.Combine(archiveDirectory, archiveName);
        if (!File.Exists(archivePath))
        {
            return archivePath;
        }

        return Path.Combine(archiveDirectory, $"{fileName}{timestamp}.{Guid.NewGuid():N}{extension}.zip");
    }

    private static async Task WaitUntilReadableAsync(string filePath, CancellationToken cancellationToken)
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
