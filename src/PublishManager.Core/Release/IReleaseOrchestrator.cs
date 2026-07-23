using PublishManager.Core.Processes;

namespace PublishManager.Core.Release;

/// <summary>
/// Runs the release pipeline for one project: preflight → compute version →
/// local build/pack steps → tag-push or workflow_dispatch → locate the triggered
/// run. Sequential, stop-on-failure, cancellable, with a dry-run mode.
/// </summary>
public interface IReleaseOrchestrator
{
    /// <summary>
    /// Executes the release. Stage transitions are reported to <paramref name="events"/>
    /// and live step output to <paramref name="log"/>. Never throws for expected
    /// failures — those come back as <see cref="ReleaseResult.Success"/> = false.
    /// </summary>
    Task<ReleaseResult> RunAsync(
        ReleaseRequest request,
        IProgress<ReleaseEvent>? events = null,
        IProgress<ProcessLine>? log = null,
        CancellationToken ct = default);
}
