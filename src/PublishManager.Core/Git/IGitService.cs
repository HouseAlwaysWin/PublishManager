namespace PublishManager.Core.Git;

/// <summary>
/// Thin wrapper over the <c>git</c> CLI (via <see cref="Processes.IProcessRunner"/>)
/// exposing exactly the operations the release pipeline needs. All paths are the
/// local working-copy directory of the managed project.
/// </summary>
public interface IGitService
{
    /// <summary>True if <paramref name="repoPath"/> is inside a git work tree.</summary>
    Task<bool> IsGitRepositoryAsync(string repoPath, CancellationToken ct = default);

    /// <summary>All tag names in the repo (unsorted; SemVer ordering is the version service's job).</summary>
    Task<IReadOnlyList<string>> ListTagsAsync(string repoPath, CancellationToken ct = default);

    /// <summary>Current branch name (e.g. "main"), or "HEAD" when detached.</summary>
    Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default);

    /// <summary>True when there are no uncommitted changes (porcelain status empty).</summary>
    Task<bool> IsWorkingTreeCleanAsync(string repoPath, CancellationToken ct = default);

    /// <summary>Commit SHA at HEAD.</summary>
    Task<string> GetHeadShaAsync(string repoPath, CancellationToken ct = default);

    /// <summary>
    /// Peels a tag to the commit it ultimately points at (<c>git rev-parse "&lt;tag&gt;^{}"</c>).
    /// Annotated tags have their own object SHA; a workflow run's head_sha is always the
    /// commit SHA, so this is required for tag→run correlation.
    /// </summary>
    Task<string> PeelTagToCommitAsync(string repoPath, string tag, CancellationToken ct = default);

    /// <summary>Parses the remote URL into owner/repo, or null if unavailable/unparseable.</summary>
    Task<RepoSlug?> GetRemoteSlugAsync(string repoPath, string remote = "origin", CancellationToken ct = default);

    /// <summary>Fetches tags from the remote so local version computation sees published tags.</summary>
    Task FetchTagsAsync(string repoPath, string remote = "origin", CancellationToken ct = default);

    /// <summary>True if the tag already exists on the remote (checked via <c>ls-remote</c>).</summary>
    Task<bool> RemoteTagExistsAsync(string repoPath, string tag, string remote = "origin", CancellationToken ct = default);

    /// <summary>Creates an annotated tag at HEAD.</summary>
    Task CreateAnnotatedTagAsync(string repoPath, string tag, string message, CancellationToken ct = default);

    /// <summary>Pushes a single tag to the remote. Uses git's own credentials (not the gh token).</summary>
    Task PushTagAsync(string repoPath, string tag, string remote = "origin", CancellationToken ct = default);
}
