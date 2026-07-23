using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Processes;

namespace PublishManager.Tests.Processes;

public class ProcessRunnerTests
{
    private static ProcessRunner CreateRunner() => new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var runner = CreateRunner();
        var lines = new List<ProcessLine>();
        var progress = new Progress<ProcessLine>(lines.Add);

        var result = await runner.RunAsync(
            new ProcessRequest { FileName = "git", Arguments = ["--version"] },
            progress);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("git version", result.StdOutText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UnknownExecutable_ThrowsProcessStartException()
    {
        var runner = CreateRunner();

        await Assert.ThrowsAsync<ProcessStartException>(() =>
            runner.RunAsync(new ProcessRequest { FileName = "this-exe-does-not-exist-42" }));
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessAndThrows()
    {
        var runner = CreateRunner();
        using var cts = new CancellationTokenSource();

        // A process that would otherwise run for 30s; cancel shortly after start.
        var request = new ProcessRequest
        {
            FileName = "powershell",
            Arguments = ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
        };

        var task = runner.RunAsync(request, progress: null, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }
}
