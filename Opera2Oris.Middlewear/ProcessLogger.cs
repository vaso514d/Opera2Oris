namespace Opera2Oris.Middlewear;

internal sealed class ProcessLogger : IDisposable
{
    private readonly LoggingSettings _settings;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private DateOnly? _writerDate;
    private bool _fileLoggingAvailable;

    public ProcessLogger(LoggingSettings settings)
    {
        _settings = settings;
        if (_settings.Enabled)
        {
            try
            {
                Directory.CreateDirectory(_settings.Directory);
                _fileLoggingAvailable = true;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"File logging disabled. Cannot create log directory '{_settings.Directory}': {exception.Message}");
            }
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now;
        var line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";

        if (level == "ERROR")
        {
            Console.Error.WriteLine(line);
        }
        else
        {
            Console.WriteLine(line);
        }

        if (!_fileLoggingAvailable)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                EnsureWriter(DateOnly.FromDateTime(timestamp.DateTime));
                _writer!.WriteLine(line);

                if (exception is not null)
                {
                    _writer.WriteLine(exception);
                }

                _writer.Flush();
            }
            catch (Exception writeException)
            {
                _fileLoggingAvailable = false;
                _writer?.Dispose();
                _writer = null;
                Console.Error.WriteLine($"File logging disabled. Cannot write log file: {writeException.Message}");
            }
        }
    }

    private void EnsureWriter(DateOnly date)
    {
        if (_writer is not null && _writerDate == date)
        {
            return;
        }

        _writer?.Dispose();
        _writerDate = date;

        var path = Path.Combine(_settings.Directory, $"{_settings.FilePrefix}-{date:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
