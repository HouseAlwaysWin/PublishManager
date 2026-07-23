namespace PublishManager.Core.GitHub;

/// <summary>
/// Talks to the GitHub Actions API (via Octokit, authenticated from
/// <see cref="IGitHubAuthProvider"/>). Triggers dispatches, correlates the run a
/// release produced, polls run/job/step status live, and fetches job logs once a
/// job completes (the REST log endpoint 404s while a job is still running).
/// </summary>
public interface IGitHubActionsService
{
    /// <summary>Resolves the current auth (account + scopes) for display/validation.</summary>
    Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default);

    /// <summary>Lists the workflows defined in a repo.</summary>
    Task<IReadOnlyList<WorkflowInfo>> ListWorkflowsAsync(string owner, string repo, CancellationToken ct = default);

    /// <summary>Triggers a <c>workflow_dispatch</c> for the given workflow file and ref.</summary>
    Task DispatchAsync(
        string owner,
        string repo,
        string workflowFile,
        string gitRef,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken ct = default);

    /// <summary>
    /// Polls for the run triggered by a release and returns its id, or null if none
    /// appears before <see cref="RunQuery.Timeout"/>. Correlates by workflow + event
    /// + (head_sha for tag-push / actor+branch for dispatch) + created-since.
    /// </summary>
    Task<long?> FindRunAsync(RunQuery query, CancellationToken ct = default);

    /// <summary>Fetches a run plus its jobs and steps (for the live monitor).</summary>
    Task<WorkflowRunSnapshot?> GetRunSnapshotAsync(string owner, string repo, long runId, CancellationToken ct = default);

    /// <summary>Fetches a completed job's full log text (returns partial/empty while running).</summary>
    Task<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken ct = default);
}
