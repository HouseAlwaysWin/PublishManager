using Microsoft.Extensions.Logging;
using PublishManager.Core.Processes;

namespace PublishManager.Core.Git;

/// <summary>Default <see cref="IGitService"/>, shelling out to <c>git</c>.</summary>
public sealed class GitService(IProcessRunner runner, ILogger<GitService> logger) : IGitService
{
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<GitService> _logger = logger;

    public async Task<bool> IsGitRepositoryAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await RunAsync(repoPath, ct, "rev-parse", "--is-inside-work-tree").ConfigureAwait(false);
        return result.Success && result.StdOutText.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> ListTagsAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await RunCheckedAsync(repoPath, ct, "tag", "--list").ConfigureAwait(false);
        return result.StdOut
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await RunCheckedAsync(repoPath, ct, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false);
        return result.StdOutText;
    }

    public async Task<bool> IsWorkingTreeCleanAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await RunCheckedAsync(repoPath, ct, "status", "--porcelain").ConfigureAwait(false);
        return result.StdOutText.Length == 0;
    }

    public async Task<string> GetHeadShaAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await RunCheckedAsync(repoPath, ct, "rev-parse", "HEAD").ConfigureAwait(false);
        return result.StdOutText;
    }

    public async Task<string> PeelTagToCommitAsync(string repoPath, string tag, CancellationToken ct = default)
    {
        // "^{}" dereferences an annotated tag down to the commit; harmless for lightweight tags.
        var result = await RunCheckedAsync(repoPath, ct, "rev-parse", $"{tag}^{{}}").ConfigureAwait(false);
        return result.StdOutText;
    }

    public async Task<RepoSlug?> GetRemoteSlugAsync(string repoPath, string remote = "origin", CancellationToken ct = default)
    {
        var result = await RunAsync(repoPath, ct, "remote", "get-url", remote).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogDebug("git remote get-url {Remote} failed in {Repo}: {Err}", remote, repoPath, result.StdErrText);
            return null;
        }

        return RemoteUrlParser.TryParse(result.StdOutText, out var slug) ? slug : null;
    }

    public async Task FetchTagsAsync(string repoPath, string remote = "origin", CancellationToken ct = default)
    {
        await RunCheckedAsync(repoPath, ct, "fetch", remote, "--tags", "--force").ConfigureAwait(false);
    }

    public async Task<bool> RemoteTagExistsAsync(string repoPath, string tag, string remote = "origin", CancellationToken ct = default)
    {
        var result = await RunCheckedAsync(repoPath, ct, "ls-remote", "--tags", remote, $"refs/tags/{tag}").ConfigureAwait(false);
        return result.StdOutText.Length > 0;
    }

    public async Task CreateAnnotatedTagAsync(string repoPath, string tag, string message, CancellationToken ct = default)
    {
        await RunCheckedAsync(repoPath, ct, "tag", "-a", tag, "-m", message).ConfigureAwait(false);
    }

    public async Task PushTagAsync(string repoPath, string tag, string remote = "origin", CancellationToken ct = default)
    {
        await RunCheckedAsync(repoPath, ct, "push", remote, tag).ConfigureAwait(false);
    }

    private Task<ProcessResult> RunAsync(string repoPath, CancellationToken ct, params string[] args)
    {
        var request = new ProcessRequest
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = repoPath,
        };
        return _runner.RunAsync(request, progress: null, ct);
    }

    private async Task<ProcessResult> RunCheckedAsync(string repoPath, CancellationToken ct, params string[] args)
    {
        var result = await RunAsync(repoPath, ct, args).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new GitException(
                $"`git {string.Join(' ', args)}` failed (exit {result.ExitCode}) in '{repoPath}': {result.StdErrText}");
        }
        return result;
    }
}
