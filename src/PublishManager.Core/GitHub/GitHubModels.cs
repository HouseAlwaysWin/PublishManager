namespace PublishManager.Core.GitHub;

/// <summary>Live status of a workflow run / job / step.</summary>
public enum RunStatus
{
    Queued,
    InProgress,
    Completed,
    Unknown,
}

/// <summary>Final conclusion of a completed run / job / step.</summary>
public enum RunConclusion
{
    None,
    Success,
    Failure,
    Cancelled,
    Skipped,
    TimedOut,
    ActionRequired,
    Neutral,
    Stale,
    Unknown,
}

/// <summary>A workflow definition in a repo.</summary>
public sealed record WorkflowInfo(long Id, string Name, string Path, string FileName);

/// <summary>Snapshot of one step within a job.</summary>
public sealed record StepSnapshot(int Number, string Name, RunStatus Status, RunConclusion Conclusion);

/// <summary>Snapshot of one job within a run, including its steps.</summary>
public sealed record JobSnapshot(
    long Id,
    string Name,
    RunStatus Status,
    RunConclusion Conclusion,
    IReadOnlyList<StepSnapshot> Steps)
{
    public bool IsCompleted => Status == RunStatus.Completed;
}

/// <summary>Snapshot of a workflow run and all of its jobs/steps at a point in time.</summary>
public sealed record WorkflowRunSnapshot(
    long Id,
    string Name,
    RunStatus Status,
    RunConclusion Conclusion,
    string HtmlUrl,
    IReadOnlyList<JobSnapshot> Jobs)
{
    public bool IsCompleted => Status == RunStatus.Completed;
}

/// <summary>Result of resolving the current GitHub authentication.</summary>
public sealed record GitHubAuthStatus(
    bool IsAuthenticated,
    string? Account,
    IReadOnlyList<string> Scopes,
    string? Source)
{
    /// <summary>The <c>workflow</c> scope is required to dispatch and to push workflow file changes.</summary>
    public bool HasWorkflowScope => Scopes.Contains("workflow");
}

/// <summary>Parameters for locating the run triggered by a release (tag-push or dispatch).</summary>
public sealed record RunQuery
{
    public required string Owner { get; init; }
    public required string Repo { get; init; }

    /// <summary>
    /// Workflow file to filter by. When null/empty, runs are matched across all
    /// workflows (by event + head_sha) — lets tag-push monitoring work without
    /// the user configuring a workflow file.
    /// </summary>
    public string? WorkflowFile { get; init; }

    /// <summary>GitHub event that triggers the run ("push" for tags, "workflow_dispatch" for dispatch).</summary>
    public required string Event { get; init; }

    /// <summary>Only consider runs created at or after this time (captured just before triggering).</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>Commit SHA the run's head_sha must match (tag-push correlation).</summary>
    public string? HeadSha { get; init; }

    /// <summary>Actor login that triggered the run (dispatch correlation).</summary>
    public string? Actor { get; init; }

    /// <summary>Branch/ref filter (dispatch correlation).</summary>
    public string? Branch { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Clock-skew tolerance applied to <see cref="Since"/> when matching created_at.</summary>
    public TimeSpan Skew { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>Thrown when no usable GitHub token is available.</summary>
public sealed class GitHubAuthException(string message) : Exception(message);

/// <summary>Maps GitHub's raw status/conclusion strings to our enums.</summary>
public static class RunStateMapper
{
    public static RunStatus MapStatus(string? value) => value switch
    {
        "queued" or "waiting" or "requested" or "pending" => RunStatus.Queued,
        "in_progress" => RunStatus.InProgress,
        "completed" => RunStatus.Completed,
        null or "" => RunStatus.Unknown,
        _ => RunStatus.Unknown,
    };

    public static RunConclusion MapConclusion(string? value) => value switch
    {
        null or "" => RunConclusion.None,
        "success" => RunConclusion.Success,
        "failure" => RunConclusion.Failure,
        "cancelled" => RunConclusion.Cancelled,
        "skipped" => RunConclusion.Skipped,
        "timed_out" => RunConclusion.TimedOut,
        "action_required" => RunConclusion.ActionRequired,
        "neutral" => RunConclusion.Neutral,
        "stale" => RunConclusion.Stale,
        _ => RunConclusion.Unknown,
    };
}
