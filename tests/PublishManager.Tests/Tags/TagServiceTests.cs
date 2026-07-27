using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Models;
using PublishManager.Core.Processes;
using PublishManager.Core.Tags;
using PublishManager.Core.Versioning;

namespace PublishManager.Tests.Tags;

public class TagServiceTests
{
    private readonly TagFakeGitService _git = new();
    private readonly TagFakeGitHubService _github = new();
    private readonly TagFakeProcessRunner _runner = new();

    private TagService CreateSut() =>
        new(_git, new SemVerService(), _github, _runner, NullLogger<TagService>.Instance);

    private static Project TestProject() => new()
    {
        Name = "App",
        LocalPath = @"C:\repo",
        TagPrefix = "v",
        Owner = "owner",
        Repo = "repo",
    };

    [Fact]
    public async Task List_OrdersNewestFirst_AndFlagsLocations()
    {
        _git.TagDetails =
        [
            new TagInfo("v1.0.0", "aaaaaaaaaaaa", DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            new TagInfo("v1.2.0", "bbbbbbbbbbbb", DateTimeOffset.Parse("2026-03-01T00:00:00Z")),
        ];
        _runner.RemoteTagLines = ["sha\trefs/tags/v1.2.0", "sha\trefs/tags/v9.9.9"];
        _github.ReleaseTags = ["v1.2.0"];

        var tags = await CreateSut().ListAsync(TestProject());

        Assert.Equal(["v9.9.9", "v1.2.0", "v1.0.0"], tags.Select(t => t.Name));

        var v12 = tags.Single(t => t.Name == "v1.2.0");
        Assert.True(v12.ExistsLocally);
        Assert.True(v12.ExistsOnRemote);
        Assert.True(v12.HasGitHubRelease);
        Assert.Equal("bbbbbbb", v12.ShortSha);

        var v10 = tags.Single(t => t.Name == "v1.0.0");
        Assert.True(v10.ExistsLocally);
        Assert.False(v10.ExistsOnRemote);   // not in ls-remote output
        Assert.False(v10.HasGitHubRelease);

        // Remote-only tag is still listed.
        Assert.False(tags.Single(t => t.Name == "v9.9.9").ExistsLocally);
    }

    [Fact]
    public async Task Delete_LocalOnly_DoesNotTouchRemoteOrRelease()
    {
        var options = new TagDeletionOptions { DeleteLocal = true, DeleteRemote = false, DeleteGitHubRelease = false };

        var result = await CreateSut().DeleteAsync(TestProject(), "v1.0.0", options);

        Assert.True(result.Success);
        Assert.Contains("v1.0.0", _git.DeletedLocalTags);
        Assert.Empty(_git.DeletedRemoteTags);
        Assert.Empty(_github.DeletedReleaseTags);
    }

    [Fact]
    public async Task Delete_AllScopes_RemovesEverywhere()
    {
        _github.ReleaseExistsForTag = true;
        var options = new TagDeletionOptions { DeleteLocal = true, DeleteRemote = true, DeleteGitHubRelease = true };

        var result = await CreateSut().DeleteAsync(TestProject(), "v1.0.0", options);

        Assert.True(result.Success);
        Assert.True(result.DeletedLocal);
        Assert.True(result.DeletedRemote);
        Assert.True(result.DeletedRelease);
        Assert.Contains("v1.0.0", _git.DeletedLocalTags);
        Assert.Contains("v1.0.0", _git.DeletedRemoteTags);
        Assert.Contains("v1.0.0", _github.DeletedReleaseTags);
    }

    [Fact]
    public async Task Delete_RemoteFails_ReportsErrorAndSkipsLocal()
    {
        _git.FailRemoteDelete = true;
        var options = new TagDeletionOptions { DeleteLocal = true, DeleteRemote = true };

        var result = await CreateSut().DeleteAsync(TestProject(), "v1.0.0", options);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.False(result.DeletedRemote);
        Assert.Empty(_git.DeletedLocalTags);   // aborted before local delete
    }

    [Fact]
    public async Task Delete_ReleaseDeletedBeforeTag_SoItDoesNotBecomeDraft()
    {
        _github.ReleaseExistsForTag = true;
        var options = new TagDeletionOptions { DeleteLocal = true, DeleteRemote = true, DeleteGitHubRelease = true };

        await CreateSut().DeleteAsync(TestProject(), "v1.0.0", options);

        Assert.Equal("release", _github.OperationOrder[0]);
        Assert.Contains("remote", _git.OperationOrder);
    }

    [Fact]
    public async Task List_NonGitRepo_ReturnsEmpty()
    {
        _git.IsRepo = false;
        Assert.Empty(await CreateSut().ListAsync(TestProject()));
    }
}

sealed class TagFakeGitService : IGitService
{
    public bool IsRepo = true;
    public List<TagInfo> TagDetails = [];
    public List<string> DeletedLocalTags = [];
    public List<string> DeletedRemoteTags = [];
    public List<string> OperationOrder = [];
    public bool FailRemoteDelete;

    public Task<bool> IsGitRepositoryAsync(string p, CancellationToken ct = default) => Task.FromResult(IsRepo);
    public Task<IReadOnlyList<string>> ListTagsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([.. TagDetails.Select(t => t.Name)]);
    public Task<IReadOnlyList<TagInfo>> ListTagDetailsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TagInfo>>(TagDetails);
    public Task<string> GetCurrentBranchAsync(string p, CancellationToken ct = default) => Task.FromResult("main");
    public Task<bool> IsWorkingTreeCleanAsync(string p, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string> GetHeadShaAsync(string p, CancellationToken ct = default) => Task.FromResult("sha");
    public Task<string> PeelTagToCommitAsync(string p, string tag, CancellationToken ct = default) => Task.FromResult("sha");
    public Task<RepoSlug?> GetRemoteSlugAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.FromResult<RepoSlug?>(new RepoSlug("owner", "repo"));
    public Task FetchTagsAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> RemoteTagExistsAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.FromResult(false);
    public Task CreateAnnotatedTagAsync(string p, string tag, string msg, CancellationToken ct = default) => Task.CompletedTask;
    public Task PushTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteLocalTagAsync(string p, string tag, CancellationToken ct = default)
    {
        OperationOrder.Add("local");
        DeletedLocalTags.Add(tag);
        return Task.CompletedTask;
    }

    public Task DeleteRemoteTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default)
    {
        OperationOrder.Add("remote");
        if (FailRemoteDelete)
            throw new GitException("remote delete failed");
        DeletedRemoteTags.Add(tag);
        return Task.CompletedTask;
    }
}

sealed class TagFakeGitHubService : IGitHubActionsService
{
    public HashSet<string> ReleaseTags = [];
    public List<string> DeletedReleaseTags = [];
    public List<string> OperationOrder = [];
    public bool ReleaseExistsForTag;

    public Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default) => Task.FromResult(new GitHubAuthStatus(true, "tester", ["repo"], "gh"));
    public Task<IReadOnlyList<WorkflowInfo>> ListWorkflowsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowInfo>>([]);
    public Task DispatchAsync(string o, string r, string wf, string gitRef, IReadOnlyDictionary<string, string> inputs, CancellationToken ct = default) => Task.CompletedTask;
    public Task<RunMatch?> FindRunAsync(RunQuery query, CancellationToken ct = default) => Task.FromResult<RunMatch?>(null);
    public Task<WorkflowRunSnapshot?> GetRunSnapshotAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult<WorkflowRunSnapshot?>(null);
    public Task<string> GetJobLogsAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<IReadOnlySet<string>> GetGitHubReleaseTagsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(ReleaseTags);

    public Task<bool> DeleteGitHubReleaseForTagAsync(string o, string r, string tag, CancellationToken ct = default)
    {
        OperationOrder.Add("release");
        if (!ReleaseExistsForTag)
            return Task.FromResult(false);
        DeletedReleaseTags.Add(tag);
        return Task.FromResult(true);
    }
}

sealed class TagFakeProcessRunner : IProcessRunner
{
    public List<string> RemoteTagLines = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress = null, CancellationToken ct = default) =>
        Task.FromResult(new ProcessResult(0, RemoteTagLines, []));
}
