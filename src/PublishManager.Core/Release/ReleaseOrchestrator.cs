using Microsoft.Extensions.Logging;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Ledger;
using PublishManager.Core.Models;
using PublishManager.Core.Processes;
using PublishManager.Core.Versioning;

namespace PublishManager.Core.Release;

/// <summary>Default <see cref="IReleaseOrchestrator"/>.</summary>
public sealed class ReleaseOrchestrator(
    IGitService git,
    ISemVerService semver,
    IProcessRunner runner,
    IGitHubActionsService github,
    IReleaseLedger ledger,
    ILogger<ReleaseOrchestrator> logger) : IReleaseOrchestrator
{
    private readonly IGitService _git = git;
    private readonly ISemVerService _semver = semver;
    private readonly IProcessRunner _runner = runner;
    private readonly IGitHubActionsService _github = github;
    private readonly IReleaseLedger _ledger = ledger;
    private readonly ILogger<ReleaseOrchestrator> _logger = logger;

    public async Task<ReleaseResult> RunAsync(
        ReleaseRequest request,
        IProgress<ReleaseEvent>? events = null,
        IProgress<ProcessLine>? log = null,
        CancellationToken ct = default)
    {
        var project = request.Project;
        var dry = request.DryRun;

        // Recorded in the ledger only once the release has actually reached
        // GitHub — before that there is no released version to remember.
        var record = new PendingRecord();

        try
        {
            // ---- Preflight ----
            Report(events, ReleaseProgressKeys.Preflight, "Preflight", ReleaseProgressStatus.Running);

            if (!await _git.IsGitRepositoryAsync(project.LocalPath, ct))
                return Fail(events, ReleaseProgressKeys.Preflight, "Preflight", "本機路徑不是 git 儲存庫。", dry);

            try
            {
                await _git.FetchTagsAsync(project.LocalPath, ct: ct);
            }
            catch (Exception ex)
            {
                log?.Report(new ProcessLine($"[warn] git fetch --tags 失敗:{ex.Message}", true));
            }

            if (!await _git.IsWorkingTreeCleanAsync(project.LocalPath, ct))
                return Fail(events, ReleaseProgressKeys.Preflight, "Preflight", "工作目錄有未提交的變更,請先 commit 或 stash。", dry);

            var branch = await _git.GetCurrentBranchAsync(project.LocalPath, ct);
            if (!string.IsNullOrWhiteSpace(project.ReleaseBranch) &&
                !string.Equals(branch, project.ReleaseBranch, StringComparison.Ordinal))
            {
                return Fail(events, ReleaseProgressKeys.Preflight, "Preflight",
                    $"目前分支為 '{branch}',需切換到 '{project.ReleaseBranch}' 才能發版。", dry);
            }

            var slug = await ResolveSlugAsync(project, ct);
            Report(events, ReleaseProgressKeys.Preflight, "Preflight", ReleaseProgressStatus.Succeeded, $"分支 {branch}");

            // ---- Version ----
            Report(events, ReleaseProgressKeys.Version, "Version", ReleaseProgressStatus.Running);
            var tags = await _git.ListTagsAsync(project.LocalPath, ct);

            string nextTag;
            string nextVersion;
            if (!string.IsNullOrWhiteSpace(request.ExplicitVersion))
            {
                var explicitVersion = _semver.ParseVersion(request.ExplicitVersion, project.TagPrefix);
                if (explicitVersion is null)
                    return Fail(events, ReleaseProgressKeys.Version, "Version", $"版號格式不正確:{request.ExplicitVersion}", dry);
                nextTag = _semver.ToTag(explicitVersion, project.TagPrefix);
                nextVersion = explicitVersion.ToString();
            }
            else
            {
                var plan = _semver.ComputeNextFromTags(tags, request.Bump, project.TagPrefix);
                nextTag = plan.NextTag;
                nextVersion = StripPrefix(nextTag, project.TagPrefix);
            }

            if (project.Trigger == ReleaseTrigger.TagPush &&
                await _git.RemoteTagExistsAsync(project.LocalPath, nextTag, ct: ct))
            {
                return Fail(events, ReleaseProgressKeys.Version, "Version", $"遠端已存在 tag {nextTag}。", dry);
            }

            var currentTag = _semver.GetLatest(tags, project.TagPrefix) is { } latest
                ? _semver.ToTag(latest, project.TagPrefix)
                : "(尚無)";
            Report(events, ReleaseProgressKeys.Version, "Version", ReleaseProgressStatus.Succeeded, $"{currentTag} → {nextTag}");

            // ---- Local build/pack steps ----
            var env = new Dictionary<string, string?>
            {
                ["RELEASE_VERSION"] = nextVersion,
                ["RELEASE_TAG"] = nextTag,
            };

            for (var i = 0; i < project.Steps.Count; i++)
            {
                var step = project.Steps[i];
                // The key is positional so a step is never confused with a stage
                // of the same name, nor with another step sharing its name.
                var key = ReleaseProgressKeys.ForStep(i);
                var label = string.IsNullOrWhiteSpace(step.Name) ? $"步驟 {i + 1}" : step.Name;

                Report(events, key, label, ReleaseProgressStatus.Running);

                var (ok, exit) = await RunStepAsync(project, step, env, log, ct);
                if (!ok && !step.ContinueOnError)
                    return Fail(events, key, label, $"步驟失敗 (exit {exit})。", dry);

                Report(events, key, label,
                    ok ? ReleaseProgressStatus.Succeeded : ReleaseProgressStatus.Skipped,
                    ok ? null : $"exit {exit},已依設定略過");
            }

            // ---- Trigger ----
            var since = DateTimeOffset.UtcNow;

            if (project.Trigger == ReleaseTrigger.TagPush)
            {
                Report(events, ReleaseProgressKeys.Tag, "Tag", ReleaseProgressStatus.Running);
                if (dry)
                {
                    Report(events, ReleaseProgressKeys.Tag, "Tag", ReleaseProgressStatus.Skipped, $"將建立並推送 {nextTag}");
                }
                else
                {
                    await _git.CreateAnnotatedTagAsync(project.LocalPath, nextTag, $"Release {nextTag}", ct);
                    await _git.PushTagAsync(project.LocalPath, nextTag, ct: ct);
                    Report(events, ReleaseProgressKeys.Tag, "Tag", ReleaseProgressStatus.Succeeded, $"已推送 {nextTag}");

                    record.MarkReached(nextTag, nextVersion, slug);
                }
            }
            else
            {
                Report(events, ReleaseProgressKeys.Dispatch, "Dispatch", ReleaseProgressStatus.Running);
                if (slug is null)
                    return Fail(events, ReleaseProgressKeys.Dispatch, "Dispatch", "無法解析 owner/repo,無法觸發 workflow_dispatch。", dry);
                if (string.IsNullOrWhiteSpace(project.WorkflowFile))
                    return Fail(events, ReleaseProgressKeys.Dispatch, "Dispatch", "未設定 Workflow 檔,無法 dispatch。", dry);

                var gitRef = string.IsNullOrWhiteSpace(project.ReleaseBranch) ? branch : project.ReleaseBranch!;
                var inputs = BuildDispatchInputs(project.DispatchInputs, nextVersion, nextTag);

                if (dry)
                {
                    Report(events, ReleaseProgressKeys.Dispatch, "Dispatch", ReleaseProgressStatus.Skipped, $"將 dispatch {project.WorkflowFile} @ {gitRef}");
                }
                else
                {
                    await _github.DispatchAsync(slug.Value.Owner, slug.Value.Repo, project.WorkflowFile!, gitRef, inputs, ct);
                    Report(events, ReleaseProgressKeys.Dispatch, "Dispatch", ReleaseProgressStatus.Succeeded, $"{project.WorkflowFile} @ {gitRef}");

                    record.MarkReached(nextTag, nextVersion, slug);
                }
            }

            // ---- Locate the triggered run ----
            long? runId = null;
            if (!dry)
            {
                if (slug is null)
                {
                    Report(events, ReleaseProgressKeys.LocateRun, "LocateRun", ReleaseProgressStatus.Skipped,
                        "無法解析 owner/repo,略過監看(可在專案設定填 Owner/Repo)");
                }
                else
                {
                    Report(events, ReleaseProgressKeys.LocateRun, "LocateRun", ReleaseProgressStatus.Running);
                    var query = await BuildRunQueryAsync(project, slug.Value, nextTag, branch, since, ct);
                    var match = await _github.FindRunAsync(query, ct);
                    runId = match?.RunId;

                    // A release watches one run. If the trigger started more than
                    // one, say which was chosen instead of quietly dropping the rest.
                    var message = match is null
                        ? "找不到觸發的 run(可稍後於 GitHub 查看)"
                        : match.CandidateCount > 1
                            ? $"run #{match.RunId}(共 {match.CandidateCount} 個符合,只監看最新的一個)"
                            : $"run #{match.RunId}";

                    Report(events, ReleaseProgressKeys.LocateRun, "LocateRun",
                        match is not null ? ReleaseProgressStatus.Succeeded : ReleaseProgressStatus.Failed,
                        message);
                }
            }

            record.RunId = runId;
            if (record.Reached)
                record.CommitSha = await TryPeelAsync(project, nextTag, ct);
            await RecordAsync(project, record, succeeded: true, error: null, ct);

            return new ReleaseResult
            {
                Success = true,
                Version = nextVersion,
                Tag = nextTag,
                Owner = slug?.Owner,
                Repo = slug?.Repo,
                RunId = runId,
                DryRun = dry,
            };
        }
        catch (OperationCanceledException)
        {
            Report(events, ReleaseProgressKeys.Cancelled, "已取消", ReleaseProgressStatus.Failed, "已取消");
            await RecordAsync(project, record, succeeded: false, "已取消發版。", CancellationToken.None);
            return new ReleaseResult { Success = false, Error = "已取消發版。", DryRun = dry };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Release failed for {Project}.", project.Name);
            await RecordAsync(project, record, succeeded: false, ex.Message, ct);
            return new ReleaseResult { Success = false, Error = ex.Message, DryRun = dry };
        }
    }

    /// <summary>What is known about a release in flight, for the ledger.</summary>
    private sealed class PendingRecord
    {
        /// <summary>True once a tag has been pushed or a workflow dispatched.</summary>
        public bool Reached;
        public string? Tag;
        public string? Version;
        public string? CommitSha;
        public long? RunId;
        public RepoSlug? Slug;

        /// <summary>Called the moment the release becomes visible outside this machine.</summary>
        public void MarkReached(string tag, string version, RepoSlug? slug)
        {
            Reached = true;
            Tag = tag;
            Version = version;
            Slug = slug;
        }
    }

    /// <summary>
    /// Records the release, but only if it reached GitHub. A run that failed
    /// preflight, or a dry run, released nothing and so is not remembered.
    /// </summary>
    private async Task RecordAsync(
        Project project, PendingRecord record, bool succeeded, string? error, CancellationToken ct)
    {
        if (!record.Reached || record.Tag is null || record.Version is null)
            return;

        try
        {
            await _ledger.AppendAsync(new LedgerEntry
            {
                ProjectId = project.Id,
                Version = record.Version,
                Tag = record.Tag,
                ReleasedAt = DateTimeOffset.UtcNow,
                Succeeded = succeeded,
                CommitSha = record.CommitSha,
                RunId = record.RunId,
                RunUrl = record.Slug is { } slug && record.RunId is { } id
                    ? $"https://github.com/{slug.Owner}/{slug.Repo}/actions/runs/{id}"
                    : null,
                Error = error,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The release itself already happened; failing to record it must not
            // turn a successful release into a reported failure.
            _logger.LogError(ex, "Failed to record the release of {Tag} in the ledger.", record.Tag);
        }
    }

    private async Task<string?> TryPeelAsync(Project project, string tag, CancellationToken ct)
    {
        try
        {
            return await _git.PeelTagToCommitAsync(project.LocalPath, tag, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the commit for {Tag}.", tag);
            return null;
        }
    }

    private async Task<RepoSlug?> ResolveSlugAsync(Project project, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(project.Owner) && !string.IsNullOrWhiteSpace(project.Repo))
            return new RepoSlug(project.Owner!, project.Repo!);
        return await _git.GetRemoteSlugAsync(project.LocalPath, ct: ct);
    }

    private async Task<(bool Ok, int Exit)> RunStepAsync(
        Project project,
        ReleaseStep step,
        IReadOnlyDictionary<string, string?> env,
        IProgress<ProcessLine>? log,
        CancellationToken ct)
    {
        var workingDir = string.IsNullOrWhiteSpace(step.WorkingDirectory)
            ? project.LocalPath
            : ResolveDirectory(project.LocalPath, step.WorkingDirectory!);

        var request = step.Interpreter switch
        {
            StepInterpreter.PowerShell => new ProcessRequest
            {
                FileName = "pwsh",
                Arguments = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", step.Command],
                WorkingDirectory = workingDir,
                Environment = env,
            },
            StepInterpreter.Bash => new ProcessRequest
            {
                FileName = "bash",
                Arguments = ["-c", step.Command],
                WorkingDirectory = workingDir,
                Environment = env,
            },
            StepInterpreter.Executable => new ProcessRequest
            {
                FileName = step.Command,
                Arguments = step.Arguments,
                WorkingDirectory = workingDir,
                Environment = env,
            },
            _ => throw new InvalidOperationException($"Unknown interpreter {step.Interpreter}."),
        };

        try
        {
            var result = await _runner.RunAsync(request, log, ct);
            return (result.Success, result.ExitCode);
        }
        catch (ProcessStartException) when (step.Interpreter == StepInterpreter.PowerShell)
        {
            // pwsh not installed → fall back to Windows PowerShell.
            var fallback = request with { FileName = "powershell" };
            var result = await _runner.RunAsync(fallback, log, ct);
            return (result.Success, result.ExitCode);
        }
    }

    private async Task<RunQuery> BuildRunQueryAsync(
        Project project,
        RepoSlug slug,
        string nextTag,
        string branch,
        DateTimeOffset since,
        CancellationToken ct)
    {
        if (project.Trigger == ReleaseTrigger.TagPush)
        {
            var commit = await _git.PeelTagToCommitAsync(project.LocalPath, nextTag, ct);
            return new RunQuery
            {
                Owner = slug.Owner,
                Repo = slug.Repo,
                WorkflowFile = string.IsNullOrWhiteSpace(project.WorkflowFile) ? null : project.WorkflowFile,
                Event = "push",
                HeadSha = commit,
                Since = since,
            };
        }

        var gitRef = string.IsNullOrWhiteSpace(project.ReleaseBranch) ? branch : project.ReleaseBranch!;
        string? actor = null;
        try { actor = (await _github.GetAuthStatusAsync(ct)).Account; }
        catch { /* actor is optional */ }

        return new RunQuery
        {
            Owner = slug.Owner,
            Repo = slug.Repo,
            WorkflowFile = project.WorkflowFile!,
            Event = "workflow_dispatch",
            Branch = gitRef,
            Actor = actor,
            Since = since,
        };
    }

    private static Dictionary<string, string> BuildDispatchInputs(
        IReadOnlyDictionary<string, string> inputs, string version, string tag) =>
        inputs.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Replace("$VERSION", version).Replace("$TAG", tag));

    private static string ResolveDirectory(string basePath, string dir) =>
        Path.IsPathRooted(dir) ? dir : Path.Combine(basePath, dir);

    private static string StripPrefix(string tag, string prefix) =>
        !string.IsNullOrEmpty(prefix) && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? tag[prefix.Length..]
            : tag;

    private static void Report(
        IProgress<ReleaseEvent>? events, string key, string label, ReleaseProgressStatus status, string? message = null) =>
        events?.Report(new ReleaseEvent(key, label, status, message));

    private static ReleaseResult Fail(
        IProgress<ReleaseEvent>? events, string key, string label, string error, bool dry)
    {
        events?.Report(new ReleaseEvent(key, label, ReleaseProgressStatus.Failed, error));
        return new ReleaseResult { Success = false, Error = error, DryRun = dry };
    }
}
