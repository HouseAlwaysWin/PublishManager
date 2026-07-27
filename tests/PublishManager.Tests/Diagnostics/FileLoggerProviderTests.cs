using Microsoft.Extensions.Logging;
using PublishManager.Core.Diagnostics;

namespace PublishManager.Tests.Diagnostics;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pm-logs-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static DateTimeOffset OnDay(int day) => new(2026, 7, day, 12, 0, 0, TimeSpan.Zero);

    private string ReadAll() =>
        string.Concat(Directory.EnumerateFiles(_dir).Select(File.ReadAllText));

    [Fact]
    public void AnErrorLeavesATrailOnDisk()
    {
        using var provider = new FileLoggerProvider(_dir, LogLevel.Warning, () => OnDay(23));
        var logger = provider.CreateLogger("ProjectStore");

        logger.LogError(new InvalidOperationException("disk full"), "Failed to save projects.");

        var contents = ReadAll();
        Assert.Contains("Failed to save projects.", contents);
        Assert.Contains("disk full", contents);
        Assert.Contains("ProjectStore", contents);
    }

    [Fact]
    public void ChatterBelowTheThresholdIsNotWritten()
    {
        using var provider = new FileLoggerProvider(_dir, LogLevel.Warning, () => OnDay(23));
        var logger = provider.CreateLogger("Noise");

        logger.LogInformation("polling run 42");
        logger.LogWarning("could not reach the remote");

        var contents = ReadAll();
        Assert.DoesNotContain("polling run 42", contents);
        Assert.Contains("could not reach the remote", contents);
    }

    [Fact]
    public void EachDayGetsItsOwnFile()
    {
        var day = 23;
        using (var provider = new FileLoggerProvider(_dir, LogLevel.Warning, () => OnDay(day)))
        {
            provider.CreateLogger("X").LogWarning("first day");
            day = 24;
            provider.CreateLogger("X").LogWarning("second day");
        }

        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void LogsOlderThanTheRetentionWindowArePrunedOnStartup()
    {
        Directory.CreateDirectory(_dir);
        var stale = Path.Combine(_dir, "publishmanager-2026-06-01.log");
        var recent = Path.Combine(_dir, "publishmanager-2026-07-22.log");
        File.WriteAllText(stale, "old");
        File.WriteAllText(recent, "recent");

        using var provider = new FileLoggerProvider(_dir, LogLevel.Warning, () => OnDay(23), retentionDays: 14);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void WritingFromManyThreadsDoesNotLoseOrTearLines()
    {
        using var provider = new FileLoggerProvider(_dir, LogLevel.Warning, () => OnDay(23));
        var logger = provider.CreateLogger("Concurrent");

        Parallel.For(0, 200, i => logger.LogWarning("line {Index}", i));

        var lines = Directory.EnumerateFiles(_dir).SelectMany(File.ReadAllLines).ToList();
        Assert.Equal(200, lines.Count(l => l.Contains("line ")));
    }
}
