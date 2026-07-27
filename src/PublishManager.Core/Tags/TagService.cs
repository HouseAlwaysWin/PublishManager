using Microsoft.Extensions.Logging;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Models;
using PublishManager.Core.Processes;
using PublishManager.Core.Versioning;

namespace PublishManager.Core.Tags;

/// <summary>Default <see cref="ITagService"/>.</summary>
public sealed class TagService(
    IGitService git,
    ISemVerService semver,
    IGitHubActionsService github,
    IProcessRunner runner,
    ILogger<TagService> logger) : ITagService
{
    private readonly IGitService _git = git;
    private readonly ISemVerService _semver = semver;
    private readonly IGitHubActionsService _github = github;
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<TagService> _logger = logger;

    public async Task<IReadOnlyList<VersionTag>> ListAsync(Project project, CancellationToken ct = default)
    {
        if (!await _git.IsGitRepositoryAsync(project.LocalPath, ct).ConfigureAwait(false))
            return [];

        var local = await _git.ListTagDetailsAsync(project.LocalPath, ct).ConfigureAwait(false);
        var remote = await ListRemoteTagNamesAsync(project.LocalPath, ct).ConfigureAwait(false);
        var releaseTags = await GetReleaseTagsAsync(project, ct).ConfigureAwait(false);

        var byName = local.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var allNames = new HashSet<string>(byName.Keys, StringComparer.Ordinal);
        allNames.UnionWith(remote);

        var tags = allNames.Select(name =>
        {
            byName.TryGetValue(name, out var info);
            var version = _semver.TryParseTag(name, project.TagPrefix);

            return new VersionTag
            {
                Name = name,
                Version = version?.ToString(),
                CommitSha = info?.CommitSha,
                Date = info?.Date,
                ExistsLocally = info is not null,
                ExistsOnRemote = remote.Contains(name),
                HasGitHubRelease = releaseTags.Contains(name),
            };
        });

        // Valid versions first (newest first), then anything unparseable by name.
        return [.. tags
            .OrderByDescending(t => t.Version is not null)
            .ThenByDescending(t => _semver.TryParseTag(t.Name, project.TagPrefix), SemVerComparer.Instance)
            .ThenByDescending(t => t.Name, StringComparer.Ordinal)];
    }

    public async Task<TagDeletionResult> DeleteAsync(
        Project project,
        string tag,
        TagDeletionOptions options,
        CancellationToken ct = default)
    {
        var deletedLocal = false;
        var deletedRemote = false;
        var deletedRelease = false;

        try
        {
            // Delete the release first: GitHub turns a release whose tag vanished
            // into a draft, which is harder to clean up afterwards.
            if (options.DeleteGitHubRelease)
            {
                var slug = await ResolveSlugAsync(project, ct).ConfigureAwait(false);
                if (slug is not null)
                    deletedRelease = await _github
                        .DeleteReleaseForTagAsync(slug.Value.Owner, slug.Value.Repo, tag, ct)
                        .ConfigureAwait(false);
            }

            if (options.DeleteRemote)
            {
                await _git.DeleteRemoteTagAsync(project.LocalPath, tag, ct: ct).ConfigureAwait(false);
                deletedRemote = true;
            }

            if (options.DeleteLocal)
            {
                await _git.DeleteLocalTagAsync(project.LocalPath, tag, ct).ConfigureAwait(false);
                deletedLocal = true;
            }

            return new TagDeletionResult(tag, true, deletedLocal, deletedRemote, deletedRelease, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tag {Tag} in {Project}.", tag, project.Name);
            return new TagDeletionResult(tag, false, deletedLocal, deletedRemote, deletedRelease, ex.Message);
        }
    }

    private async Task<RepoSlug?> ResolveSlugAsync(Project project, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(project.Owner) && !string.IsNullOrWhiteSpace(project.Repo))
            return new RepoSlug(project.Owner!, project.Repo!);
        return await _git.GetRemoteSlugAsync(project.LocalPath, ct: ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<string>> GetReleaseTagsAsync(Project project, CancellationToken ct)
    {
        try
        {
            var slug = await ResolveSlugAsync(project, ct).ConfigureAwait(false);
            if (slug is null)
                return new HashSet<string>(StringComparer.Ordinal);
            return await _github.GetReleaseTagsAsync(slug.Value.Owner, slug.Value.Repo, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not resolve GitHub releases for {Project}.", project.Name);
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Remote tag names via <c>ls-remote</c>; empty when the remote is unreachable.</summary>
    private async Task<IReadOnlySet<string>> ListRemoteTagNamesAsync(string repoPath, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var result = await _runner.RunAsync(
                new ProcessRequest
                {
                    FileName = "git",
                    Arguments = ["ls-remote", "--tags", "origin"],
                    WorkingDirectory = repoPath,
                },
                progress: null,
                ct).ConfigureAwait(false);

            if (!result.Success)
                return names;

            foreach (var line in result.StdOut)
            {
                var idx = line.IndexOf("refs/tags/", StringComparison.Ordinal);
                if (idx < 0)
                    continue;

                var name = line[(idx + "refs/tags/".Length)..].Trim();
                // "^{}" lines are the dereferenced commit of an annotated tag.
                if (name.EndsWith("^{}", StringComparison.Ordinal))
                    name = name[..^3];
                if (name.Length > 0)
                    names.Add(name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "git ls-remote failed for {Repo}.", repoPath);
        }

        return names;
    }

    /// <summary>Orders SemVer values, treating null (unparseable) as lowest.</summary>
    private sealed class SemVerComparer : IComparer<Semver.SemVersion?>
    {
        public static readonly SemVerComparer Instance = new();

        public int Compare(Semver.SemVersion? x, Semver.SemVersion? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => x.CompareSortOrderTo(y),
        };
    }
}
