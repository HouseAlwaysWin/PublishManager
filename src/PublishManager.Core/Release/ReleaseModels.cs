using PublishManager.Core.Models;
using PublishManager.Core.Versioning;

namespace PublishManager.Core.Release;

/// <summary>Status of a single stage in the release pipeline.</summary>
public enum ReleaseProgressStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>Stable keys for the fixed stages every release passes through.</summary>
public static class ReleaseProgressKeys
{
    public const string Preflight = "stage:preflight";
    public const string Version = "stage:version";
    public const string Tag = "stage:tag";
    public const string Dispatch = "stage:dispatch";
    public const string LocateRun = "stage:locate-run";
    public const string Cancelled = "stage:cancelled";

    /// <summary>Key for the user's Nth configured step (zero-based).</summary>
    public static string ForStep(int index) => $"step:{index}";
}

/// <summary>
/// A progress report from a release, for one stage or one step.
/// <paramref name="Key"/> identifies the row and is stable; <paramref name="Label"/>
/// is only for display, so a step may safely be named after a built-in stage.
/// </summary>
public sealed record ReleaseEvent(
    string Key,
    string Label,
    ReleaseProgressStatus Status,
    string? Message = null);

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

    /// <summary>
    /// Branch, tag, or commit to cut the release from. Null releases whatever is
    /// checked out. Naming one never changes the working copy.
    /// </summary>
    public string? Source { get; init; }

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
