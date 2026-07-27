using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Ledger;
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
    private readonly FakeReleaseLedger _ledger = new();

    private ReleaseOrchestrator CreateSut() =>
        new(_git, new SemVerService(), _runner, _github, _ledger, NullLogger<ReleaseOrchestrator>.Instance);

    private static Project TagPushProject(params ReleaseStep[] steps) => new()
    {
        Name = "App",
        LocalPath = @"C:\repo",
        Trigger = ReleaseTrigger.TagPush,
        TagPrefix = "v",
        DefaultBump = VersionBump.Patch,
        ReleaseBranch = "main",
        WorkflowFile = "release.yml",
        Steps = [.. steps],
    };

    private static ReleaseStep PsStep(string name = "build") =>
        new() { Name = name, Interpreter = StepInterpreter.PowerShell, Command = "echo hi" };

    [Fact]
    public async Task DryRun_RunsTheStepsButWithholdsTheIrreversibleParts()
    {
        // A dry run answers "would this release succeed?", so the local steps
        // must actually run — only the outward effects are withheld.
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Patch, DryRun = true };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal("v1.0.1", result.Tag);
        Assert.Equal("1.0.1", result.Version);
        Assert.Single(_runner.Requests);        // the step ran
        Assert.Empty(_git.CreatedTags);         // no tag created
        Assert.Empty(_git.PushedTags);          // no tag pushed
        Assert.Null(result.RunId);              // no run located
    }

    [Fact]
    public async Task DryRun_FailingStep_FailsTheRelease()
    {
        _git.Tags = ["v1.0.0"];
        _runner.ExitCode = 1;
        var request = new ReleaseRequest { Project = TagPushProject(PsStep()), Bump = VersionBump.Patch, DryRun = true };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Empty(_git.PushedTags);
    }

    [Fact]
    public async Task DryRun_DoesNotDispatch()
    {
        _git.Tags = ["v1.0.0"];
        var project = TagPushProject();
        project.Trigger = ReleaseTrigger.WorkflowDispatch;
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch, DryRun = true };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal(0, _github.DispatchCount);
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
        project.Trigger = ReleaseTrigger.WorkflowDispatch;
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
    public async Task ByDefault_TheTagLandsOnWhateverIsCheckedOut()
    {
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        await CreateSut().RunAsync(request);

        Assert.Equal(("v1.0.1", null), Assert.Single(_git.CreatedTagTargets));
    }

    [Fact]
    public async Task AnExplicitSource_PutsTheTagOnThatCommitWithoutCheckingItOut()
    {
        _git.Tags = ["v1.0.0"];
        _git.Resolvable["release/1.x"] = "cafebabe1234";
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,
            Source = "release/1.x",
        };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal(("v1.0.1", "cafebabe1234"), Assert.Single(_git.CreatedTagTargets));
        Assert.Empty(_git.CheckedOut);   // the working copy is never moved
    }

    [Fact]
    public async Task AnUnknownSource_FailsBeforeAnythingIsTagged()
    {
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,
            Source = "no-such-branch",
        };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Contains("no-such-branch", result.Error);
        Assert.Empty(_git.CreatedTags);
    }

    [Fact]
    public async Task AnExplicitSource_LiftsTheMustBeOnThisBranchRule()
    {
        // The rule exists because the release follows the working copy. Naming a
        // source says where to release from, so standing elsewhere is fine.
        _git.Tags = ["v1.0.0"];
        _git.Branch = "some-feature";           // project requires "main"
        _git.Resolvable["main"] = "deadbeef99";
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,
            Source = "main",
        };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Contains("v1.0.1", _git.PushedTags);
    }

    [Fact]
    public async Task TheSourceCommitIsWhatGetsRecorded()
    {
        _git.Tags = ["v1.0.0"];
        _git.Resolvable["release/1.x"] = "cafebabe1234";
        var request = new ReleaseRequest
        {
            Project = TagPushProject(),
            Bump = VersionBump.Patch,
            Source = "release/1.x",
        };

        await CreateSut().RunAsync(request);

        Assert.Equal("cafebabe1234", Assert.Single(_ledger.Entries).CommitSha);
    }

    [Fact]
    public async Task DispatchingFromABareCommit_IsRefusedBecauseGitHubCannotDoIt()
    {
        // workflow_dispatch takes a branch or tag ref, never a commit sha.
        _git.Tags = ["v1.0.0"];
        _git.Resolvable["cafebabe1234"] = "cafebabe1234";
        var project = TagPushProject();
        project.Trigger = ReleaseTrigger.WorkflowDispatch;
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch, Source = "cafebabe1234" };

        var result = await CreateSut().RunAsync(request);

        Assert.False(result.Success);
        Assert.Equal(0, _github.DispatchCount);
    }

    [Fact]
    public async Task DispatchingFromANamedBranch_UsesItAsTheRef()
    {
        _git.Tags = ["v1.0.0"];
        _git.Resolvable["release/1.x"] = "cafebabe1234";
        var project = TagPushProject();
        project.Trigger = ReleaseTrigger.WorkflowDispatch;
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch, Source = "release/1.x" };

        var result = await CreateSut().RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal("release/1.x", Assert.Single(_github.DispatchedRefs));
    }

    [Fact]
    public async Task StepsBuildTheWorkingCopy_SoReleasingElsewhereWarns()
    {
        _git.Tags = ["v1.0.0"];
        _git.HeadSha = "aaaaaaa";
        _git.Resolvable["release/1.x"] = "cafebabe1234";
        var request = new ReleaseRequest
        {
            Project = TagPushProject(PsStep()),
            Bump = VersionBump.Patch,
            Source = "release/1.x",
        };
        var events = new List<ReleaseEvent>();

        var result = await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        Assert.True(result.Success);
        var preflight = events.Last(e => e.Key == ReleaseProgressKeys.Preflight);
        Assert.Contains("工作目錄", preflight.Message);
    }

    [Fact]
    public async Task WhenSeveralRunsMatch_TheChoiceIsReportedRatherThanMadeSilently()
    {
        // A release watches one run. If the tag started more than one, say so —
        // otherwise the extra runs are invisible.
        _git.Tags = ["v1.0.0"];
        _github.RunIdToReturn = 4242;
        _github.CandidateCount = 3;
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };
        var events = new List<ReleaseEvent>();

        await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        var locate = events.Last(e => e.Key == ReleaseProgressKeys.LocateRun);
        Assert.Contains("3", locate.Message);
    }

    [Fact]
    public async Task ASingleMatchingRunIsReportedPlainly()
    {
        _git.Tags = ["v1.0.0"];
        _github.RunIdToReturn = 4242;
        _github.CandidateCount = 1;
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };
        var events = new List<ReleaseEvent>();

        await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        var locate = events.Last(e => e.Key == ReleaseProgressKeys.LocateRun);
        Assert.Equal(ReleaseProgressStatus.Succeeded, locate.Status);
        Assert.DoesNotContain("其他", locate.Message ?? "");
    }

    [Fact]
    public async Task ASuccessfulRelease_IsRecordedInTheLedger()
    {
        _git.Tags = ["v1.0.0"];
        _git.PeeledCommit = "deadbeefcafe";
        _github.RunIdToReturn = 4242;
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        await CreateSut().RunAsync(request);

        var entry = Assert.Single(_ledger.Entries);
        Assert.Equal("v1.0.1", entry.Tag);
        Assert.Equal("1.0.1", entry.Version);
        Assert.True(entry.Succeeded);
        Assert.Equal(4242, entry.RunId);
        Assert.Equal("deadbeefcafe", entry.CommitSha);
    }

    [Fact]
    public async Task ADryRunIsNotRecorded()
    {
        // Nothing left the machine, so there is nothing to remember.
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch, DryRun = true };

        await CreateSut().RunAsync(request);

        Assert.Empty(_ledger.Entries);
    }

    [Fact]
    public async Task AReleaseThatNeverReachedGitHub_IsNotRecorded()
    {
        _git.Clean = false;   // fails preflight, long before anything is pushed
        var request = new ReleaseRequest { Project = TagPushProject(), Bump = VersionBump.Patch };

        await CreateSut().RunAsync(request);

        Assert.Empty(_ledger.Entries);
    }

    [Fact]
    public async Task StepNamedAfterABuiltInStage_StaysItsOwnRow()
    {
        // A step's name is a label, not an identity. Naming one "Tag" must not
        // merge it into the built-in Tag stage and overwrite that status.
        _git.Tags = ["v1.0.0"];
        var request = new ReleaseRequest { Project = TagPushProject(PsStep("Tag")), Bump = VersionBump.Patch };
        var events = new List<ReleaseEvent>();

        await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        var stageEvents = events.Where(e => e.Key == ReleaseProgressKeys.Tag).ToList();
        var stepEvents = events.Where(e => e.Key != ReleaseProgressKeys.Tag && e.Label == "Tag").ToList();

        Assert.NotEmpty(stageEvents);
        Assert.NotEmpty(stepEvents);
    }

    [Fact]
    public async Task TwoStepsSharingAName_GetDistinctKeys()
    {
        _git.Tags = ["v1.0.0"];
        var project = TagPushProject(PsStep("build"), PsStep("build"));
        var request = new ReleaseRequest { Project = project, Bump = VersionBump.Patch };
        var events = new List<ReleaseEvent>();

        await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        var keys = events.Where(e => e.Label == "build").Select(e => e.Key).Distinct().ToList();
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task UnnamedStep_IsLabelledByItsPosition()
    {
        _git.Tags = ["v1.0.0"];
        var blank = new ReleaseStep { Name = "", Interpreter = StepInterpreter.PowerShell, Command = "echo hi" };
        var request = new ReleaseRequest { Project = TagPushProject(blank), Bump = VersionBump.Patch };
        var events = new List<ReleaseEvent>();

        await CreateSut().RunAsync(request, new SyncProgress<ReleaseEvent>(events.Add));

        Assert.Contains(events, e => e.Label == "步驟 1");
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

sealed class FakeReleaseLedger : IReleaseLedger
{
    public List<LedgerEntry> Entries = [];

    public Task<IReadOnlyList<LedgerEntry>> ListAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>([.. Entries.Where(e => e.ProjectId == projectId)]);

    public Task AppendAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>Reports on the calling thread, so assertions see every event.</summary>
sealed class SyncProgress<T>(Action<T> on) : IProgress<T>
{
    public void Report(T value) => on(value);
}

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
    public List<(string Tag, string? Target)> CreatedTagTargets = [];
    public List<string> PushedTags = [];
    public List<string> CheckedOut = [];
    public Dictionary<string, string> Resolvable = [];

    public Task<bool> IsGitRepositoryAsync(string p, CancellationToken ct = default) => Task.FromResult(IsRepo);
    public Task<IReadOnlyList<string>> ListTagsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Tags);
    public Task<string> GetCurrentBranchAsync(string p, CancellationToken ct = default) => Task.FromResult(Branch);
    public Task<bool> IsWorkingTreeCleanAsync(string p, CancellationToken ct = default) => Task.FromResult(Clean);
    public Task<string> GetHeadShaAsync(string p, CancellationToken ct = default) => Task.FromResult(HeadSha);
    public Task<string> PeelTagToCommitAsync(string p, string tag, CancellationToken ct = default) => Task.FromResult(PeeledCommit);
    public Task<RepoSlug?> GetRemoteSlugAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.FromResult(Slug);
    public Task FetchTagsAsync(string p, string remote = "origin", CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> RemoteTagExistsAsync(string p, string tag, string remote = "origin", CancellationToken ct = default) => Task.FromResult(RemoteTagExists);
    public Task CreateAnnotatedTagAsync(string p, string tag, string msg, string? target = null, CancellationToken ct = default)
    {
        CreatedTags.Add(tag);
        CreatedTagTargets.Add((tag, target));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListBranchesAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> ResolveCommitAsync(string p, string rev, CancellationToken ct = default) =>
        Task.FromResult(Resolvable.TryGetValue(rev, out var sha) ? sha : null);
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
    public int CandidateCount = 1;
    public int DispatchCount;
    public List<RunQuery> Queries = [];
    public List<IReadOnlyDictionary<string, string>> DispatchedInputs = [];
    public List<string> DispatchedRefs = [];
    public GitHubAuthStatus Status = new(true, "tester", ["repo", "workflow"], "gh");

    public Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);
    public Task<IReadOnlyList<WorkflowInfo>> ListWorkflowsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowInfo>>([]);
    public Task DispatchAsync(string o, string r, string wf, string gitRef, IReadOnlyDictionary<string, string> inputs, CancellationToken ct = default)
    {
        DispatchCount++;
        DispatchedInputs.Add(inputs);
        DispatchedRefs.Add(gitRef);
        return Task.CompletedTask;
    }
    public Task<RunMatch?> FindRunAsync(RunQuery query, CancellationToken ct = default)
    {
        Queries.Add(query);
        return Task.FromResult(RunIdToReturn is { } id ? new RunMatch(id, CandidateCount) : null);
    }
    public Task<WorkflowRunSnapshot?> GetRunSnapshotAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult<WorkflowRunSnapshot?>(null);
    public Task<string> GetJobLogsAsync(string o, string r, long id, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<IReadOnlySet<string>> GetGitHubReleaseTagsAsync(string o, string r, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    public Task<bool> DeleteGitHubReleaseForTagAsync(string o, string r, string tag, CancellationToken ct = default) => Task.FromResult(false);
}
