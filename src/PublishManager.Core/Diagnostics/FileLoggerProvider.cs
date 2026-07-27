using System.Text;
using Microsoft.Extensions.Logging;

namespace PublishManager.Core.Diagnostics;

/// <summary>
/// Appends warnings and errors to a dated file. The app is a WinExe, so the
/// default console logger writes nowhere and a failure would otherwise vanish
/// without trace — this is what makes "it silently didn't save" diagnosable.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimum;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _gate = new();

    public FileLoggerProvider(
        string directory,
        LogLevel minimum = LogLevel.Warning,
        Func<DateTimeOffset>? clock = null,
        int retentionDays = 14)
    {
        _directory = directory;
        _minimum = minimum;
        _clock = clock ?? (() => DateTimeOffset.Now);

        Directory.CreateDirectory(_directory);
        PruneOlderThan(retentionDays);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimum;

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = _clock();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(LevelLabel(level)).Append("] ")
            .Append(category).Append(" — ")
            .Append(message);

        if (exception is not null)
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

        var path = Path.Combine(_directory, $"publishmanager-{now:yyyy-MM-dd}.log");

        // Serialised so concurrent writers cannot interleave within a line.
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, line.Append(Environment.NewLine).ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never be the reason an operation fails.
            }
        }
    }

    private void PruneOlderThan(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        var cutoff = _clock().Date.AddDays(-retentionDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "publishmanager-*.log"))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)["publishmanager-".Length..];
                if (DateTime.TryParse(stamp, out var date) && date < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Housekeeping only — never worth failing startup over.
        }
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
