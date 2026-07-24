using System.Collections.ObjectModel;
using Avalonia.Media;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private RunStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private RunConclusion _conclusion;
    [ObservableProperty] private string? _htmlUrl;
    [ObservableProperty] private string _logText = string.Empty;

    public ObservableCollection<JobLineViewModel> Jobs { get; } = [];

    public string StatusGlyph => RunGlyph.For(Status, Conclusion);
    public IBrush StatusBrush => RunGlyph.BrushFor(Status, Conclusion);

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

    /// <summary>
    /// Stops polling and clears everything on display. Called when the selected
    /// project changes — otherwise the previous project's run stays on screen,
    /// frozen, and looks like it belongs to the newly selected project.
    /// </summary>
    public void Reset()
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
        HasRun = false;
        IsMonitoring = false;
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
                    AppendLog($"\n===== {job.Name} =====\n{log.TrimEnd()}\n");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch logs for job {JobId}.", job.Id);
            }
        }
    }

    /// <summary>
    /// Appends to the log, keeping only the tail. A single CI job log can be
    /// 100KB+, and the bound TextBox is not virtualized — letting it grow
    /// unbounded across jobs freezes the UI.
    /// </summary>
    private void AppendLog(string text)
    {
        var combined = LogText.Length == 0 ? text : LogText + text;

        if (combined.Length > MaxLogChars)
        {
            var start = combined.Length - MaxLogChars;
            var newline = combined.IndexOf('\n', start);
            combined = TruncationNotice + combined[(newline >= 0 ? newline + 1 : start)..];
        }

        LogText = combined;
    }

    private const int MaxLogChars = 80_000;
    private const string TruncationNotice = "…（較早的輸出已截斷,完整內容請按「在 GitHub 開啟」）…\n";
}
