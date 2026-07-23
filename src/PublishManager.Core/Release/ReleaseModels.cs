using PublishManager.Core.Models;
using PublishManager.Core.Versioning;

namespace PublishManager.Core.Release;

/// <summary>Status of a single stage in the release pipeline.</summary>
public enum ReleaseStageStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>A pipeline stage transition, streamed to the UI as the release progresses.</summary>
public sealed record ReleaseEvent(string Stage, ReleaseStageStatus Status, string? Message = null);

/// <summary>Inputs for one release run.</summary>
public sealed record ReleaseRequest
{
    public required Project Project { get; init; }
    public required VersionBump Bump { get; init; }

    /// <summary>
    /// Explicit version to release (prefix optional). When set, this overrides
    /// <see cref="Bump"/>-based computation from tags.
    /// </summary>
    public string? ExplicitVersion { get; init; }

    /// <summary>When true, compute and run everything except side effects (tag push / dispatch).</summary>
    public bool DryRun { get; init; }
}

/// <summary>Outcome of a release run, including the run to monitor (if one was located).</summary>
public sealed record ReleaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public string? Version { get; init; }
    public string? Tag { get; init; }

    public string? Owner { get; init; }
    public string? Repo { get; init; }
    public long? RunId { get; init; }

    public bool DryRun { get; init; }

    /// <summary>True when there is a located workflow run the UI can start monitoring.</summary>
    public bool HasRunToMonitor => RunId is not null && Owner is not null && Repo is not null;
}
