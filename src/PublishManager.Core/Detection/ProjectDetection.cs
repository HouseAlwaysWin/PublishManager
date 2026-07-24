using PublishManager.Core.Git;
using PublishManager.Core.Models;

namespace PublishManager.Core.Detection;

/// <summary>A workflow file found under <c>.github/workflows</c> and the triggers it declares.</summary>
public sealed record WorkflowFileInfo(string FileName, bool TriggersOnTagPush, bool TriggersOnDispatch);

/// <summary>Everything that could be inferred about a project from its local folder.</summary>
public sealed record ProjectDetection
{
    public bool IsGitRepository { get; init; }

    public ProjectKind Kind { get; init; } = ProjectKind.Script;

    /// <summary>owner/repo parsed from the git remote, if any.</summary>
    public RepoSlug? Slug { get; init; }

    public string? CurrentBranch { get; init; }

    /// <summary>Prefix inferred from existing tags (e.g. "v" from "v1.2.3"); null when undeterminable.</summary>
    public string? TagPrefix { get; init; }

    public IReadOnlyList<WorkflowFileInfo> Workflows { get; init; } = [];

    /// <summary>Best workflow to use for triggering/monitoring, if one stands out.</summary>
    public string? SuggestedWorkflowFile { get; init; }

    /// <summary>Release model implied by the suggested workflow's triggers.</summary>
    public ReleaseModel? SuggestedReleaseModel { get; init; }
}
