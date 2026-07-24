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
    public async Task Detects_DotNet_TagWorkflow_Slug_Branch_AndTagPrefix()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
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
        Assert.Equal(ProjectKind.DotNet, detection.Kind);
        Assert.Equal("HouseAlwaysWin", detection.Slug!.Value.Owner);
        Assert.Equal("PublishManager", detection.Slug!.Value.Repo);
        Assert.Equal("main", detection.CurrentBranch);
        Assert.Equal("v", detection.TagPrefix);
        Assert.Equal("release.yml", detection.SuggestedWorkflowFile);   // tag trigger wins over dispatch
        Assert.Equal(ReleaseModel.TagPush, detection.SuggestedReleaseModel);
    }

    [Fact]
    public async Task Detects_Script_AndDispatchModel_WhenOnlyDispatchWorkflow()
    {
        WriteWorkflow("deploy.yml", "on:\n  workflow_dispatch:\n    inputs:\n      version:\n");

        var detection = await CreateSut(new StubGitService { IsRepo = false }).DetectAsync(_dir);

        Assert.Equal(ProjectKind.Script, detection.Kind);
        Assert.Equal("deploy.yml", detection.SuggestedWorkflowFile);
        Assert.Equal(ReleaseModel.WorkflowDispatch, detection.SuggestedReleaseModel);
        Assert.False(detection.IsGitRepository);
    }

    [Fact]
    public async Task FindsDotNetProject_InNestedSrcFolder()
    {
        var nested = Path.Combine(_dir, "src", "App");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "App.csproj"), "<Project />");

        var detection = await CreateSut(new StubGitService()).DetectAsync(_dir);

        Assert.Equal(ProjectKind.DotNet, detection.Kind);
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
}
