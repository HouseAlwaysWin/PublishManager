using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PublishManager.Core.Processes;

/// <summary>
/// Default <see cref="IProcessRunner"/>. Uses two concurrent
/// <see cref="TextReader.ReadLineAsync(CancellationToken)"/> pumps (no deadlock
/// on full pipes), UTF-8, and <see cref="ProcessStartInfo.ArgumentList"/> to
/// avoid quoting bugs. On cancellation it kills the entire process tree so
/// nested msbuild/dotnet/script children die too.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger = logger;

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessLine>? progress = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };

        foreach (var arg in request.Arguments)
            psi.ArgumentList.Add(arg);

        if (request.Environment is not null)
            foreach (var kv in request.Environment)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi };

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Process.Start returned false.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start process {FileName}", request.FileName);
            throw new ProcessStartException(request.FileName, ex);
        }

        var stdout = new List<string>();
        var stderr = new List<string>();

        async Task PumpAsync(TextReader reader, bool isError, List<string> sink)
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                sink.Add(line);
                progress?.Report(new ProcessLine(line, isError));
            }
        }

        var pumpTask = Task.WhenAll(
            PumpAsync(proc.StandardOutput, isError: false, stdout),
            PumpAsync(proc.StandardError, isError: true, stderr));

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            await pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(proc);
            // Let the pumps unwind so their exceptions are observed, not orphaned.
            try { await pumpTask.ConfigureAwait(false); } catch { /* already cancelling */ }
            throw;
        }

        return new ProcessResult(proc.ExitCode, stdout, stderr);
    }

    private void TryKillTree(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process tree for {FileName}.", proc.StartInfo.FileName);
        }
    }
}
