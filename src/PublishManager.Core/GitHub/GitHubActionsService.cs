using Microsoft.Extensions.Logging;
using Octokit;

namespace PublishManager.Core.GitHub;

/// <summary>Default <see cref="IGitHubActionsService"/> backed by Octokit.</summary>
public sealed class GitHubActionsService(IGitHubAuthProvider auth, ILogger<GitHubActionsService> logger)
    : IGitHubActionsService
{
    private static readonly ProductHeaderValue Product = new("PublishManager", "0.1");

    private readonly IGitHubAuthProvider _auth = auth;
    private readonly ILogger<GitHubActionsService> _logger = logger;

    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private string? _cachedToken;
    private GitHubClient? _client;

    public async Task<GitHubAuthStatus> GetAuthStatusAsync(CancellationToken ct = default)
    {
        var token = await _auth.GetTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
            return new GitHubAuthStatus(false, null, [], null);

        try
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var user = await client.User.Current().ConfigureAwait(false);
            var scopes = client.GetLastApiInfo()?.OauthScopes ?? [];
            return new GitHubAuthStatus(true, user.Login, scopes, "gh");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve GitHub auth status.");
            return new GitHubAuthStatus(false, null, [], null);
        }
    }

    public async Task<IReadOnlyList<WorkflowInfo>> ListWorkflowsAsync(string owner, string repo, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        var response = await client.Actions.Workflows.List(owner, repo).ConfigureAwait(false);
        return response.Workflows
            .Select(w => new WorkflowInfo(w.Id, w.Name, w.Path, FileNameOf(w.Path)))
            .ToList();
    }

    public async Task DispatchAsync(
        string owner,
        string repo,
        string workflowFile,
        string gitRef,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        var dispatch = new CreateWorkflowDispatch(gitRef);
        if (inputs.Count > 0)
            dispatch.Inputs = inputs.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await client.Actions.Workflows.CreateDispatch(owner, repo, workflowFile, dispatch).ConfigureAwait(false);
    }

    public async Task<long?> FindRunAsync(RunQuery query, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);

        var request = new WorkflowRunsRequest { Event = query.Event };
        if (!string.IsNullOrEmpty(query.HeadSha)) request.HeadSha = query.HeadSha;
        if (!string.IsNullOrEmpty(query.Actor)) request.Actor = query.Actor;
        if (!string.IsNullOrEmpty(query.Branch)) request.Branch = query.Branch;

        var options = new ApiOptions { PageSize = 20, PageCount = 1 };
        var cutoff = query.Since - query.Skew;
        var deadline = DateTimeOffset.UtcNow + query.Timeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // With a workflow file, filter to it; otherwise match across all
                // workflows (by event + head_sha) so monitoring works unconfigured.
                var response = string.IsNullOrWhiteSpace(query.WorkflowFile)
                    ? await client.Actions.Workflows.Runs
                        .List(query.Owner, query.Repo, request, options).ConfigureAwait(false)
                    : await client.Actions.Workflows.Runs
                        .ListByWorkflow(query.Owner, query.Repo, query.WorkflowFile, request, options).ConfigureAwait(false);

                var match = response.WorkflowRuns
                    .Where(r => r.CreatedAt >= cutoff)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefault();

                if (match is not null)
                    return match.Id;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error while polling for the triggered run.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                _logger.LogWarning("Timed out locating the run for {Owner}/{Repo} {Workflow}.",
                    query.Owner, query.Repo, query.WorkflowFile);
                return null;
            }

            await Task.Delay(query.PollInterval, ct).ConfigureAwait(false);
        }
    }

    public async Task<WorkflowRunSnapshot?> GetRunSnapshotAsync(string owner, string repo, long runId, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);

        var run = await client.Actions.Workflows.Runs.Get(owner, repo, runId).ConfigureAwait(false);
        var jobs = await client.Actions.Workflows.Jobs.List(owner, repo, runId).ConfigureAwait(false);

        var jobSnapshots = jobs.Jobs.Select(MapJob).ToList();

        return new WorkflowRunSnapshot(
            run.Id,
            run.Name ?? string.Empty,
            RunStateMapper.MapStatus(run.Status.StringValue),
            RunStateMapper.MapConclusion(run.Conclusion?.StringValue),
            run.HtmlUrl,
            jobSnapshots);
    }

    public async Task<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        try
        {
            return await client.Actions.Workflows.Jobs.GetLogs(owner, repo, jobId).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            // Logs 404 while the job is still running; treat as "not ready yet".
            return string.Empty;
        }
    }

    private static JobSnapshot MapJob(WorkflowJob job) => new(
        job.Id,
        job.Name,
        RunStateMapper.MapStatus(job.Status.StringValue),
        RunStateMapper.MapConclusion(job.Conclusion?.StringValue),
        job.Steps.Select(s => new StepSnapshot(
            s.Number,
            s.Name,
            RunStateMapper.MapStatus(s.Status.StringValue),
            RunStateMapper.MapConclusion(s.Conclusion?.StringValue))).ToList());

    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private async Task<GitHubClient> GetClientAsync(CancellationToken ct)
    {
        var token = await _auth.GetTokenAsync(ct).ConfigureAwait(false)
            ?? throw new GitHubAuthException("找不到 GitHub token。請執行 `gh auth login` 或設定 PAT。");

        await _clientGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is null || _cachedToken != token)
            {
                _client = new GitHubClient(Product) { Credentials = new Credentials(token) };
                _cachedToken = token;
            }
            return _client;
        }
        finally
        {
            _clientGate.Release();
        }
    }
}
