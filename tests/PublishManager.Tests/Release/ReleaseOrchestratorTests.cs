using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Models;
using PublishManager.Core.Processes;
using PublishManager.Core.Release;
using PublishManager.Core.Versioning;

namespace PublishManager.Tests.Release;

public class ReleaseOrchestratorTests
{
    private readonly FakeGitService _git = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeGitHubActionsService _github = new();

    private ReleaseOrchestrator CreateSut() =>
        new(_git, new SemVerService(), _runner, _github, NullLogger<ReleaseOrchestrator>.Instance);

    private static Project TagPushProject(params ReleaseStep[] steps) => new()
    {
        Name = "App",
        LocalPath = @"C:\repo",
        ReleaseModel = ReleaseModel.TagPush,
        TagPrefix = "v",
        DefaultBump = VersionBump.Patch,
        ReleaseBranch = "main",
        WorkflowFile = "release.yml",
        Steps = [.. steps],
    };

    private static ReleaseStep PsStep(string name = "build") =>
        new() { Name = name, Interpreter = StepInterpreter.PowerShell, Command = "echo hi" };

    [Fact]
    public async Task DryRun_ComputesVersion_ButSkipsSideEffects()
    {
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Patch, DryRun = true };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal("v1.0.1", result.Tag);
        Assert.Equal("1.0.1", result.Version);
        Assert.Empty(_runner.Requests);        // step skipped
        Assert.Empty(_git.CreatedTags);         // no tag created
        Assert.Empty(_git.PushedTags);          // no tag pushed
        Assert.Null(result.RunId);              // no run located
    }

    [Fact]
    public async Task TagPush_HappyPath_RunsStep_TagsPushes_AndLocatesRun()
    {
        _git.Tags = ["v1.2.0"];
        _git.PeeledCommit = "deadbeef";
        _github.RunIdToReturn = 4242;
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal("v1.2.1", result.Tag);
        Assert.Single(_runner.Requests);
        Assert.Equal("pwsh", _runner.Requests[0].FileName);
        Assert.Contains("v1.2.1", _git.CreatedTags);
        Assert.Contains("v1.2.1", _git.PushedTags);
        Assert.Equal(4242, result.RunId);
        var query = Assert.Single(_github.Queries);
        Assert.Equal("push", query.Event);
        Assert.Equal("deadbeef", query.HeadSha);
    }

    [Fact]
    public async Task StepInjectsReleaseVersionEnv()
    {
        _git.Tags = ["v2.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Minor };

        await CreateSut().RunAsync(request);

        var env = _runner.Requests[0].Environment!;
        Assert.Equal("2.1.0", env["RELEASE_VERSION"]);
        Assert.Equal("v2.1.0", env["RELEASE_TAG"]);
    }

    [Fact]
    public async Task DirtyWorkingTree_Fails_NoTag()
    {
        _git.Clean = false;
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Contains("未提交", result.Error);
        Assert.Empty(_git.CreatedTags);
    }

    [Fact]
    public async Task WrongBranch_Fails()
    {
        _git.Branch = "develop";
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Contains("develop", result.Error);
    }

    [Fact]
    public async Task RemoteTagExists_Fails_NoTagCreated()
    {
        _git.Tags = ["v1.0.0"];
        _git.RemoteTagExists = true;
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Contains("已存在", result.Error);
        Assert.Empty(_git.CreatedTags);
    }

    [Fact]
    public async Task StepFailure_StopsPipeline_NoTag()
    {
        _git.Tags = ["v1.0.0"];
        _runner.ExitCode = 1; // step fails
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Empty(_git.CreatedTags);   // aborted before tagging
        Assert.Empty(_git.PushedTags);
    }

    [Fact]
    public async Task StepFailure_ContinueOnError_ProceedsToTag()
    {
        _git.Tags = ["v1.0.0"];
        _runner.ExitCode = 1;
        var step = PsStep();
        step.ContinueOnError = true;
        var request = new ReleaseRequest { Project = TagPushProject(step), Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Contains("v1.0.1", _git.PushedTags);
    }

    [Fact]
    public async Task Dispatch_SubstitutesVersionTokens_AndDispatches()
    {
        _git.Tags = ["v3.0.0"];
        var project = TagPushProject();
        project.ReleaseModel = ReleaseModel.WorkflowDispatch;
        project.DispatchInputs["version"] = "$VERSION";
        project.DispatchInputs["ref_tag"] = "$TAG";
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal(1, _github.DispatchCount);
        var inputs = Assert.Single(_github.DispatchedInputs);
        Assert.Equal("3.0.1", inputs["version"]);
        Assert.Equal("v3.0.1", inputs["ref_tag"]);
        Assert.Empty(_git.CreatedTags); // dispatch model does not tag locally
    }

    [Fact]
    public async Task ExplicitVersion_OverridesBump()
    {
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,   // would be v1.0.1, but explicit wins
            ExplicitVersion = "2.5.0",
        };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal("v2.5.0", result.Tag);
        Assert.Equal("2.5.0", result.Version);
        Assert.Contains("v2.5.0", _git.PushedTags);
    }

    [Fact]
    public async Task ExplicitVersion_Invalid_Fails()
    {
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,
            ExplicitVersion = "not-a-version",
        };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Contains("版號格式不正確", result.Error);
        Assert.Empty(_git.CreatedTags);
    }

    [Fact]
    public async Task TagPush_NoWorkflowFile_StillLocatesRunByHeadSha()
    {
        _git.Tags = ["v1.0.0"];
        _github.RunIdToReturn = 77;
        var project = TagPushProject();
        project.WorkflowFile = null;   // workflow file not configured
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal(77, result.RunId);
        var query = Assert.Single(_github.Queries);
        Assert.Null(query.WorkflowFile);   // matched across all workflows
        Assert.Equal("push", query.Event);
    }
}

// ---- Hand-rolled fakes (no mocking framework) ----

sealed class FakeGitService : IGitService
{
    public bool IsRepo = true;
    public bool Clean = true;
    public string Branch = "main";
    public List<string> Tags = [];
    public bool RemoteTagExists;
    public RepoSlug? Slug = new("owner", "repo");
    public string HeadSha = "abc123";
    public string PeeledCommit = "abc123";
    public List<string> CreatedTags = [];
    public List<string> PushedTags = [];

    public Task<bool> IsGitRepositoryAsync(string p, CancellationToken ct = default) => Task.FromResult(IsRepo);
    public Task<IReadOnlyList<string>> ListTagsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Tags);
    public Task<string> GetCurrentBranchAsync(string p, CancellationToken ct = default) => Task.FromResult(Branch);
    public Task<bool> IsWorkingTreeCleanAsync(string p, CancellationToken ct = default) => Task.FromResult(Clean);
    public Task<string> GetHeadShaAsync(string p, CancellationToken ct = default) => Task.FromResult(HeadSha);
    public Task<string> PeelTagToCommitAsync(string p, string tag, CancellationToken ct = default) => Task.FromResult(PeeledCommit);
    public Task<RepoSlug?> GetRemoteSlugAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.FromResult(Slug);
    public Task FetchTagsAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> RemoteTagExistsAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.FromResult(RemoteTagExists);
    public Task CreateAnnotatedTagAsync(string p, string tag, string msg, CancellationToken ct = default) { CreatedTags.Add(tag); return Task.CompletedTask; }
    public Task PushTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) { PushedTags.Add(tag); return Task.CompletedTask; }
    public Task DeleteLocalTagAsync(string p, string tag, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteRemoteTagAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<TagInfo>> ListTagDetailsAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TagInfo>>([.. Tags.Select(t => new TagInfo(t, "sha", null))]);
}

sealed class FakeProcessRunner : IProcessRunner
{
    public int ExitCode;
    public List<ProcessRequest> Requests = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress = null, CancellationToken ct = default)
    {
        Requests.Add(request);
        progress?.Report(new ProcessLine($"ran {request.FileName}", false));
        return Task.FromResult(new ProcessResult(ExitCode, ["out"], []));
    }
}

sealed class FakeGitHubActionsService : IGitHubActionsService
{
    public long? RunIdToReturn = 999;
    public int DispatchCount;
    public List<RunQuery> Queries = [];
    public List<IReadOnlyDictionary<string, string>> DispatchedInputs = [];
    public GitHubAuthStatus Status = new(true, "tester", ["repo", "workflow"], "gh");

    public Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);
    public Task<IReadOnlyList<WorkflowInfo>> ListWorkflowsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowInfo>>([]);
    public Task DispatchAsync(string o, string r, string wf, string gitRef, IReadOnlyDictionary<string, string> inputs, CancellationToken ct = default)
    {
        DispatchCount++;
        DispatchedInputs.Add(inputs);
        return Task.CompletedTask;
    }
    public Task<long?> FindRunAsync(RunQuery query, CancellationToken ct = default) { Queries.Add(query); return Task.FromResult(RunIdToReturn); }
    public Task<WorkflowRunSnapshot?> GetRunSnapshotAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult<WorkflowRunSnapshot?>(null);
    public Task<string> GetJobLogsAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<IReadOnlySet<string>> GetReleaseTagsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    public Task<bool> DeleteReleaseForTagAsync(string o, string r, string tag, CancellationToken ct = default) => Task.FromResult(false);
}
