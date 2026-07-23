using Microsoft.Extensions.Logging;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
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
    ILogger<ReleaseOrchestrator> logger) : IReleaseOrchestrator
{
    private readonly IGitService _git = git;
    private readonly ISemVerService _semver = semver;
    private readonly IProcessRunner _runner = runner;
    private readonly IGitHubActionsService _github = github;
    private readonly ILogger<ReleaseOrchestrator> _logger = logger;

    public async Task<ReleaseResult> RunAsync(
        ReleaseRequest request,
        IProgress<ReleaseEvent>? events = null,
        IProgress<ProcessLine>? log = null,
        CancellationToken ct = default)
    {
        var project = request.Project;
        var dry = request.DryRun;

        try
        {
            // ---- Preflight ----
            Report(events, "Preflight", ReleaseStageStatus.Running);

            if (!await _git.IsGitRepositoryAsync(project.LocalPath, ct))
                return Fail(events, "Preflight", "本機路徑不是 git 儲存庫。", dry);

            try
            {
                await _git.FetchTagsAsync(project.LocalPath, ct: ct);
            }
            catch (Exception ex)
            {
                log?.Report(new ProcessLine($"[warn] git fetch --tags 失敗:{ex.Message}", true));
            }

            if (!await _git.IsWorkingTreeCleanAsync(project.LocalPath, ct))
                return Fail(events, "Preflight", "工作目錄有未提交的變更,請先 commit 或 stash。", dry);

            var branch = await _git.GetCurrentBranchAsync(project.LocalPath, ct);
            if (!string.IsNullOrWhiteSpace(project.ReleaseBranch) &&
                !string.Equals(branch, project.ReleaseBranch, StringComparison.Ordinal))
            {
                return Fail(events, "Preflight",
                    $"目前分支為 '{branch}',需切換到 '{project.ReleaseBranch}' 才能發版。", dry);
            }

            var slug = await ResolveSlugAsync(project, ct);
            Report(events, "Preflight", ReleaseStageStatus.Succeeded, $"分支 {branch}");

            // ---- Version ----
            Report(events, "Version", ReleaseStageStatus.Running);
            var tags = await _git.ListTagsAsync(project.LocalPath, ct);

            string nextTag;
            string nextVersion;
            if (!string.IsNullOrWhiteSpace(request.ExplicitVersion))
            {
                var explicitVersion = _semver.ParseVersion(request.ExplicitVersion, project.TagPrefix);
                if (explicitVersion is null)
                    return Fail(events, "Version", $"版號格式不正確:{request.ExplicitVersion}", dry);
                nextTag = _semver.ToTag(explicitVersion, project.TagPrefix);
                nextVersion = explicitVersion.ToString();
            }
            else
            {
                var plan = _semver.ComputeNextFromTags(tags, request.Bump, project.TagPrefix);
                nextTag = plan.NextTag;
                nextVersion = StripPrefix(nextTag, project.TagPrefix);
            }

            if (project.ReleaseModel == ReleaseModel.TagPush &&
                await _git.RemoteTagExistsAsync(project.LocalPath, nextTag, ct: ct))
            {
                return Fail(events, "Version", $"遠端已存在 tag {nextTag}。", dry);
            }

            var currentTag = _semver.GetLatest(tags, project.TagPrefix) is { } latest
                ? _semver.ToTag(latest, project.TagPrefix)
                : "(尚無)";
            Report(events, "Version", ReleaseStageStatus.Succeeded, $"{currentTag} → {nextTag}");

            // ---- Local build/pack steps ----
            var env = new Dictionary<string, string?>
            {
                ["RELEASE_VERSION"] = nextVersion,
                ["RELEASE_TAG"] = nextTag,
            };

            foreach (var step in project.Steps)
            {
                var stageName = string.IsNullOrWhiteSpace(step.Name) ? "step" : step.Name;
                Report(events, stageName, ReleaseStageStatus.Running);

                if (dry)
                {
                    Report(events, stageName, ReleaseStageStatus.Skipped, "dry-run 不執行");
                    continue;
                }

                var (ok, exit) = await RunStepAsync(project, step, env, log, ct);
                if (!ok && !step.ContinueOnError)
                    return Fail(events, stageName, $"步驟失敗 (exit {exit})。", dry);

                Report(events, stageName,
                    ok ? ReleaseStageStatus.Succeeded : ReleaseStageStatus.Skipped,
                    ok ? null : $"exit {exit},已依設定略過");
            }

            // ---- Trigger ----
            var since = DateTimeOffset.UtcNow;

            if (project.ReleaseModel == ReleaseModel.TagPush)
            {
                Report(events, "Tag", ReleaseStageStatus.Running);
                if (dry)
                {
                    Report(events, "Tag", ReleaseStageStatus.Skipped, $"將建立並推送 {nextTag}");
                }
                else
                {
                    await _git.CreateAnnotatedTagAsync(project.LocalPath, nextTag, $"Release {nextTag}", ct);
                    await _git.PushTagAsync(project.LocalPath, nextTag, ct: ct);
                    Report(events, "Tag", ReleaseStageStatus.Succeeded, $"已推送 {nextTag}");
                }
            }
            else
            {
                Report(events, "Dispatch", ReleaseStageStatus.Running);
                if (slug is null)
                    return Fail(events, "Dispatch", "無法解析 owner/repo,無法觸發 workflow_dispatch。", dry);
                if (string.IsNullOrWhiteSpace(project.WorkflowFile))
                    return Fail(events, "Dispatch", "未設定 Workflow 檔,無法 dispatch。", dry);

                var gitRef = string.IsNullOrWhiteSpace(project.ReleaseBranch) ? branch : project.ReleaseBranch!;
                var inputs = BuildDispatchInputs(project.DispatchInputs, nextVersion, nextTag);

                if (dry)
                {
                    Report(events, "Dispatch", ReleaseStageStatus.Skipped, $"將 dispatch {project.WorkflowFile} @ {gitRef}");
                }
                else
                {
                    await _github.DispatchAsync(slug.Value.Owner, slug.Value.Repo, project.WorkflowFile!, gitRef, inputs, ct);
                    Report(events, "Dispatch", ReleaseStageStatus.Succeeded, $"{project.WorkflowFile} @ {gitRef}");
                }
            }

            // ---- Locate the triggered run ----
            long? runId = null;
            if (!dry)
            {
                if (slug is null)
                {
                    Report(events, "LocateRun", ReleaseStageStatus.Skipped,
                        "無法解析 owner/repo,略過監看(可在專案設定填 Owner/Repo)");
                }
                else
                {
                    Report(events, "LocateRun", ReleaseStageStatus.Running);
                    var query = await BuildRunQueryAsync(project, slug.Value, nextTag, branch, since, ct);
                    runId = await _github.FindRunAsync(query, ct);
                    Report(events, "LocateRun",
                        runId is not null ? ReleaseStageStatus.Succeeded : ReleaseStageStatus.Failed,
                        runId is not null ? $"run #{runId}" : "找不到觸發的 run(可稍後於 GitHub 查看)");
                }
            }

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
            Report(events, "Cancelled", ReleaseStageStatus.Failed, "已取消");
            return new ReleaseResult { Success = false, Error = "已取消發版。", DryRun = dry };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Release failed for {Project}.", project.Name);
            return new ReleaseResult { Success = false, Error = ex.Message, DryRun = dry };
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
        if (project.ReleaseModel == ReleaseModel.TagPush)
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

    private static void Report(IProgress<ReleaseEvent>? events, string stage, ReleaseStageStatus status, string? message = null) =>
        events?.Report(new ReleaseEvent(stage, status, message));

    private static ReleaseResult Fail(IProgress<ReleaseEvent>? events, string stage, string error, bool dry)
    {
        events?.Report(new ReleaseEvent(stage, ReleaseStageStatus.Failed, error));
        return new ReleaseResult { Success = false, Error = error, DryRun = dry };
    }
}
