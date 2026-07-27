using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Detection;
using PublishManager.Core.Git;
using PublishManager.Core.Models;

namespace PublishManager.Tests.Detection;

public sealed class ProjectDetectorTests : IDisposable
{
    private readonly string _dir;

    public ProjectDetectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pm-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ProjectDetector CreateSut(StubGitService git) =>
        new(git, NullLogger<ProjectDetector>.Instance);

    private void WriteWorkflow(string name, string content)
    {
        var dir = Path.Combine(_dir, ".github", "workflows");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    [Fact]
    public async Task Detects_TagWorkflow_Slug_Branch_AndTagPrefix()
    {
        WriteWorkflow("ci.yml", "on:\n  workflow_dispatch:\n");
        WriteWorkflow("release.yml", "on:\n  push:\n    tags:\n      - 'v*'\n");

        var git = new StubGitService
        {
            IsRepo = true,
            Slug = new RepoSlug("HouseAlwaysWin", "PublishManager"),
            Branch = "main",
            Tags = ["v1.0.0", "v1.1.0"],
        };

        var detection = await CreateSut(git).DetectAsync(_dir);

        Assert.True(detection.IsGitRepository);
        Assert.Equal("HouseAlwaysWin", detection.Slug!.Value.Owner);
        Assert.Equal("PublishManager", detection.Slug!.Value.Repo);
        Assert.Equal("main", detection.CurrentBranch);
        Assert.Equal("v", detection.TagPrefix);
        Assert.Equal("release.yml", detection.SuggestedWorkflowFile);   // tag trigger wins over dispatch
        Assert.Equal(ReleaseTrigger.TagPush, detection.SuggestedTrigger);
        Assert.Empty(detection.UnwatchedTagWorkflows);
    }

    [Fact]
    public async Task Detects_DispatchTrigger_WhenOnlyDispatchWorkflow()
    {
        WriteWorkflow("deploy.yml", "on:\n  workflow_dispatch:\n    inputs:\n      version:\n");

        var detection = await CreateSut(new StubGitService { IsRepo = false }).DetectAsync(_dir);

        Assert.Equal("deploy.yml", detection.SuggestedWorkflowFile);
        Assert.Equal(ReleaseTrigger.WorkflowDispatch, detection.SuggestedTrigger);
        Assert.False(detection.IsGitRepository);
    }

    [Fact]
    public async Task ReportsTheTagWorkflowsThatWouldGoUnwatched()
    {
        // A release watches one run, so a second tag-triggered workflow would
        // run unseen. It must be named rather than silently dropped.
        WriteWorkflow("release.yml", "on:\n  push:\n    tags:\n      - 'v*'\n");
        WriteWorkflow("docs.yml", "on:\n  push:\n    tags:\n      - 'v*'\n");

        var detection = await CreateSut(new StubGitService()).DetectAsync(_dir);

        var unwatched = Assert.Single(detection.UnwatchedTagWorkflows);
        Assert.NotEqual(detection.SuggestedWorkflowFile, unwatched);
        Assert.Contains(unwatched, new[] { "release.yml", "docs.yml" });
    }

    [Fact]
    public async Task InfersNonVeePrefix_FromExistingTags()
    {
        var git = new StubGitService { IsRepo = true, Tags = ["release-1.0.0", "release-1.1.0", "v9.9.9"] };

        var detection = await CreateSut(git).DetectAsync(_dir);

        Assert.Equal("release-", detection.TagPrefix);   // most common lead-in
    }

    [Fact]
    public async Task MissingPath_ReturnsEmptyDetection()
    {
        var detection = await CreateSut(new StubGitService()).DetectAsync(Path.Combine(_dir, "does-not-exist"));

        Assert.False(detection.IsGitRepository);
        Assert.Empty(detection.Workflows);
        Assert.Null(detection.SuggestedWorkflowFile);
    }
}

sealed class StubGitService : IGitService
{
    public bool IsRepo;
    public RepoSlug? Slug;
    public string Branch = "main";
    public List<string> Tags = [];

    public Task<bool> IsGitRepositoryAsync(string p, CancellationToken ct = default) => Task.FromResult(IsRepo);
    public Task<IReadOnlyList<string>> ListTagsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Tags);
    public Task<string> GetCurrentBranchAsync(string p, CancellationToken ct = default) => Task.FromResult(Branch);
    public Task<bool> IsWorkingTreeCleanAsync(string p, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string> GetHeadShaAsync(string p, CancellationToken ct = default) => Task.FromResult("sha");
    public Task<string> PeelTagToCommitAsync(string p, string tag, CancellationToken ct = default) => Task.FromResult("sha");
    public Task<RepoSlug?> GetRemoteSlugAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.FromResult(Slug);
    public Task FetchTagsAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> RemoteTagExistsAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.FromResult(false);
    public Task CreateAnnotatedTagAsync(string p, string tag, string msg, CancellationToken ct = default) => Task.CompletedTask;
    public Task PushTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteLocalTagAsync(string p, string tag, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteRemoteTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<TagInfo>> ListTagDetailsAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TagInfo>>([.. Tags.Select(t => new TagInfo(t, "sha", null))]);
}
