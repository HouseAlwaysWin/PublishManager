using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Git;
using PublishManager.Core.Models;
using PublishManager.Core.Processes;
using PublishManager.Core.Release;
using PublishManager.Core.Versioning;

namespace PublishManager.ViewModels;

/// <summary>One stage row in the release pipeline progress list.</summary>
public partial class ReleaseStageViewModel(string name) : ViewModelBase
{
    public string Name { get; } = name;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(Glyph))] private ReleaseStageStatus _status;
    [ObservableProperty] private string? _message;

    public string Glyph => Status switch
    {
        ReleaseStageStatus.Succeeded => "✓",
        ReleaseStageStatus.Failed => "✗",
        ReleaseStageStatus.Running => "▶",
        ReleaseStageStatus.Skipped => "–",
        _ => "•",
    };
}

/// <summary>
/// Drives the release panel for the selected project: version preview, dry-run,
/// the Release/Cancel commands, live pipeline progress + log, and the embedded
/// GitHub Actions run monitor.
/// </summary>
public partial class ReleaseViewModel : ViewModelBase
{
    private readonly IReleaseOrchestrator _orchestrator;
    private readonly IGitService _git;
    private readonly ISemVerService _semver;
    private readonly ILogger<ReleaseViewModel> _logger;
    private CancellationTokenSource? _cts;

    public ReleaseViewModel(
        IReleaseOrchestrator orchestrator,
        IGitService git,
        ISemVerService semver,
        WorkflowRunMonitorViewModel monitor,
        ILogger<ReleaseViewModel> logger)
    {
        _orchestrator = orchestrator;
        _git = git;
        _semver = semver;
        _logger = logger;
        Monitor = monitor;
    }

    public WorkflowRunMonitorViewModel Monitor { get; }

    public ObservableCollection<ReleaseStageViewModel> Stages { get; } = [];
    public VersionBump[] Bumps { get; } = Enum.GetValues<VersionBump>();

    [ObservableProperty] private Project? _project;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ReleaseCommand))] private bool _hasProject;
    [ObservableProperty] private string? _currentVersion;
    [ObservableProperty] private string? _branch;
    [ObservableProperty] private VersionBump _bump;
    [ObservableProperty] private string? _nextVersionPreview;
    [ObservableProperty] private bool _useManualVersion;
    [ObservableProperty] private string _manualVersion = string.Empty;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ReleaseCommand))] private bool _isReleasing;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _logText = string.Empty;

    /// <summary>Reconfigures the panel for a newly selected project (or clears it when null).</summary>
    public void SetProject(Project? project)
    {
        CancelInternal();
        Monitor.Stop();

        Project = project;
        HasProject = project is not null;
        Stages.Clear();
        LogText = string.Empty;
        StatusMessage = null;
        CurrentVersion = null;
        Branch = null;
        NextVersionPreview = null;
        UseManualVersion = false;
        ManualVersion = string.Empty;
        Bump = project?.DefaultBump ?? VersionBump.Patch;

        _ = LoadInfoAsync();
    }

    partial void OnBumpChanged(VersionBump value) => _ = UpdatePreviewAsync();
    partial void OnUseManualVersionChanged(bool value) => _ = UpdatePreviewAsync();
    partial void OnManualVersionChanged(string value) => _ = UpdatePreviewAsync();

    private async Task LoadInfoAsync()
    {
        var project = Project;
        if (project is null)
            return;

        try
        {
            if (!await _git.IsGitRepositoryAsync(project.LocalPath))
            {
                CurrentVersion = "(非 git 儲存庫)";
                return;
            }

            Branch = await _git.GetCurrentBranchAsync(project.LocalPath);
            var tags = await _git.ListTagsAsync(project.LocalPath);
            var latest = _semver.GetLatest(tags, project.TagPrefix);
            CurrentVersion = latest is null ? "(尚無 tag)" : _semver.ToTag(latest, project.TagPrefix);
            NextVersionPreview = _semver.ComputeNextFromTags(tags, Bump, project.TagPrefix).NextTag;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load release info for {Project}.", project.Name);
        }
    }

    private async Task UpdatePreviewAsync()
    {
        var project = Project;
        if (project is null)
            return;

        try
        {
            if (UseManualVersion)
            {
                var version = _semver.ParseVersion(ManualVersion, project.TagPrefix);
                NextVersionPreview = version is null
                    ? "(版號格式不正確)"
                    : _semver.ToTag(version, project.TagPrefix);
                return;
            }

            var tags = await _git.ListTagsAsync(project.LocalPath);
            NextVersionPreview = _semver.ComputeNextFromTags(tags, Bump, project.TagPrefix).NextTag;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Preview update failed.");
        }
    }

    private bool CanRelease => HasProject && !IsReleasing;

    [RelayCommand(CanExecute = nameof(CanRelease))]
    private async Task ReleaseAsync()
    {
        var project = Project;
        if (project is null)
            return;

        Stages.Clear();
        LogText = string.Empty;
        StatusMessage = null;
        Monitor.Stop();
        IsReleasing = true;
        _cts = new CancellationTokenSource();

        var events = new Progress<ReleaseEvent>(OnReleaseEvent);
        var log = new Progress<ProcessLine>(OnLogLine);

        try
        {
            var request = new ReleaseRequest
            {
                Project = project,
                Bump = Bump,
                ExplicitVersion = UseManualVersion ? ManualVersion : null,
                DryRun = DryRun,
            };
            var result = await _orchestrator.RunAsync(request, events, log, _cts.Token);

            StatusMessage = result.Success
                ? result.DryRun
                    ? $"Dry-run 完成:下一版將是 {result.Tag}"
                    : $"發版成功:{result.Tag}"
                : $"發版失敗:{result.Error}";

            if (result.Success && !result.DryRun)
            {
                if (result.HasRunToMonitor)
                    _ = Monitor.MonitorAsync(result.Owner!, result.Repo!, result.RunId!.Value);
                await LoadInfoAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Release run threw.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsReleasing = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelInternal();
        Monitor.Stop();
        StatusMessage = "已要求取消…";
    }

    private void CancelInternal()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void OnReleaseEvent(ReleaseEvent e)
    {
        var stage = Stages.FirstOrDefault(s => s.Name == e.Stage);
        if (stage is null)
        {
            stage = new ReleaseStageViewModel(e.Stage);
            Stages.Add(stage);
        }
        stage.Status = e.Status;
        stage.Message = e.Message;
    }

    private void OnLogLine(ProcessLine line) =>
        LogText += (line.IsError ? "[err] " : string.Empty) + line.Text + "\n";
}
