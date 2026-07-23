namespace PublishManager.Core.Processes;

/// <summary>
/// Runs external processes (git / gh / dotnet / user scripts) with live,
/// line-by-line streaming of stdout and stderr, cancellation, custom working
/// directory and environment, and a captured result. This is the shared
/// backbone for git operations, GitHub CLI calls, and release script steps.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts <paramref name="request"/>, streaming each output line to
    /// <paramref name="progress"/> as it is read, and returns the exit code
    /// together with the full captured output once the process exits.
    /// </summary>
    /// <exception cref="ProcessStartException">The process could not be started.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled; the process tree is killed first.
    /// </exception>
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessLine>? progress = null,
        CancellationToken ct = default);
}
