using System.Text.Json.Serialization;
using PublishManager.Core.Versioning;

namespace PublishManager.Core.Models;

/// <summary>How a project starts its workflow run.</summary>
public enum ReleaseTrigger
{
    /// <summary>Bump version locally, create &amp; push a git tag; the tag triggers the workflow.</summary>
    TagPush,

    /// <summary>Trigger a cloud workflow directly via the GitHub API (workflow_dispatch).</summary>
    WorkflowDispatch,
}

/// <summary>Which interpreter runs a release step's command.</summary>
public enum StepInterpreter
{
    /// <summary>Run as a PowerShell script (pwsh, falling back to powershell.exe).</summary>
    PowerShell,

    /// <summary>Run as a bash script.</summary>
    Bash,

    /// <summary>Run an executable directly with the given arguments.</summary>
    Executable,
}

/// <summary>
/// One command the user configures a project to run locally during a release.
/// Expected to be repeatable and free of external side effects, so a dry run
/// can execute it.
/// </summary>
public sealed class ReleaseStep
{
    public string Name { get; set; } = string.Empty;

    public StepInterpreter Interpreter { get; set; } = StepInterpreter.PowerShell;

    /// <summary>Inline script body (PowerShell/Bash) or executable path (Executable).</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Arguments passed to the executable (Executable interpreter only).</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Working directory; null/empty means the project's local path.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>If true, a non-zero exit does not abort the release.</summary>
    public bool ContinueOnError { get; set; }
}

/// <summary>
/// One release line the app manages: where its repository sits, and how that
/// line is versioned and released. A repository may host several, told apart by
/// tag prefix.
/// </summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the local git working copy.</summary>
    public string LocalPath { get; set; } = string.Empty;

    // Persisted under its old name so existing projects.json files keep their
    // trigger instead of silently falling back to the default.
    [JsonPropertyName("releaseModel")]
    public ReleaseTrigger Trigger { get; set; } = ReleaseTrigger.TagPush;

    /// <summary>Tag prefix used for version tags (e.g. "v").</summary>
    public string TagPrefix { get; set; } = "v";

    /// <summary>Default bump applied when releasing.</summary>
    public VersionBump DefaultBump { get; set; } = VersionBump.Patch;

    /// <summary>Branch the release must be on; null/empty means "whatever is checked out".</summary>
    public string? ReleaseBranch { get; set; }

    /// <summary>Explicit owner override; null derives it from the git remote.</summary>
    public string? Owner { get; set; }

    /// <summary>Explicit repo override; null derives it from the git remote.</summary>
    public string? Repo { get; set; }

    /// <summary>Workflow file name (e.g. "release.yml") to dispatch and/or monitor.</summary>
    public string? WorkflowFile { get; set; }

    /// <summary>Ordered local release steps (build/pack/etc.).</summary>
    public List<ReleaseStep> Steps { get; set; } = [];

    /// <summary>Inputs sent with workflow_dispatch (WorkflowDispatch trigger).</summary>
    public Dictionary<string, string> DispatchInputs { get; set; } = [];
}
