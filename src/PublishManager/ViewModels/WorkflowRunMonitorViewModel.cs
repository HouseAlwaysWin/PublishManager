using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PublishManager.Core.GitHub;

namespace PublishManager.ViewModels;

/// <summary>
/// Live monitor for a single workflow run. Polls run/job/step status on an
/// interval and, as each job completes, pulls that job's full log (the REST log
/// endpoint 404s while a job runs, so logs arrive job-by-job on completion).
/// </summary>
public partial class WorkflowRunMonitorViewModel : ViewModelBase
{
    private readonly IGitHubActionsService _actions;
    private readonly ILogger<WorkflowRunMonitorViewModel> _logger;
    private readonly Dictionary<long, JobLineViewModel> _jobIndex = [];
    private readonly HashSet<long> _loggedJobs = [];
    private CancellationTokenSource? _cts;

    public WorkflowRunMonitorViewModel(IGitHubActionsService actions, ILogger<WorkflowRunMonitorViewModel> logger)
    {
        _actions = actions;
        _logger = logger;
    }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    [ObservableProperty] private bool _hasRun;
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private string? _runName;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(StatusGlyph))] private RunStatus _status;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(StatusGlyph))] private RunConclusion _conclusion;
    [ObservableProperty] private string? _htmlUrl;
    [ObservableProperty] private string _logText = string.Empty;

    public ObservableCollection<JobLineViewModel> Jobs { get; } = [];

    public string StatusGlyph => RunGlyph.For(Status, Conclusion);

    /// <summary>Begins monitoring the given run until it completes or is cancelled.</summary>
    public async Task MonitorAsync(string owner, string repo, long runId, CancellationToken external = default)
    {
        Reset();
        HasRun = true;
        IsMonitoring = true;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var ct = _cts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                WorkflowRunSnapshot? snapshot = null;
                try
                {
                    snapshot = await _actions.GetRunSnapshotAsync(owner, repo, runId, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Polling run {RunId} failed; will retry.", runId);
                }

                if (snapshot is not null)
                {
                    Apply(snapshot);
                    await FetchCompletedJobLogsAsync(owner, repo, snapshot, ct);
                    if (snapshot.IsCompleted)
                        break;
                }

                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped intentionally
        }
        finally
        {
            IsMonitoring = false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Reset()
    {
        Stop();
        _cts?.Dispose();
        _cts = null;
        Jobs.Clear();
        _jobIndex.Clear();
        _loggedJobs.Clear();
        LogText = string.Empty;
        RunName = null;
        Status = RunStatus.Unknown;
        Conclusion = RunConclusion.None;
        HtmlUrl = null;
    }

    private void Apply(WorkflowRunSnapshot snapshot)
    {
        RunName = snapshot.Name;
        Status = snapshot.Status;
        Conclusion = snapshot.Conclusion;
        HtmlUrl = snapshot.HtmlUrl;

        foreach (var job in snapshot.Jobs)
        {
            if (_jobIndex.TryGetValue(job.Id, out var existing))
            {
                existing.Update(job);
            }
            else
            {
                var vm = new JobLineViewModel(job);
                _jobIndex[job.Id] = vm;
                Jobs.Add(vm);
            }
        }
    }

    private async Task FetchCompletedJobLogsAsync(string owner, string repo, WorkflowRunSnapshot snapshot, CancellationToken ct)
    {
        foreach (var job in snapshot.Jobs)
        {
            if (!job.IsCompleted || !_loggedJobs.Add(job.Id))
                continue;

            try
            {
                var log = await _actions.GetJobLogsAsync(owner, repo, job.Id, ct);
                if (!string.IsNullOrWhiteSpace(log))
                    LogText += $"\n===== {job.Name} =====\n{log.TrimEnd()}\n";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch logs for job {JobId}.", job.Id);
            }
        }
    }
}
