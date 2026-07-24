using System.Collections.ObjectModel;
using Avalonia.Media;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private ReleaseStageStatus _status;

    [ObservableProperty] private string? _message;

    public string Glyph => Status switch
    {
        ReleaseStageStatus.Succeeded => "✓",
        ReleaseStageStatus.Failed => "✗",
        ReleaseStageStatus.Running => "●",
        ReleaseStageStatus.Skipped => "–",
        _ => "○",
    };

    public IBrush GlyphBrush => Status switch
    {
        ReleaseStageStatus.Succeeded => RunGlyph.Success,
        ReleaseStageStatus.Failed => RunGlyph.Failure,
        ReleaseStageStatus.Running => RunGlyph.Running,
        ReleaseStageStatus.Skipped => RunGlyph.Muted,
        _ => RunGlyph.Pending,
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

    /// <summary>Suppresses preview refreshes while <see cref="SetProject"/> resets fields.</summary>
    private bool _suspendPreview;

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

        // Clear the monitor, not just stop it — otherwise the previous project's
        // run stays on screen (frozen) under the newly selected project.
        Monitor.Reset();

        // Resetting these fields would otherwise fire several redundant,
        // git-backed preview refreshes; LoadInfoAsync does the work once.
        _suspendPreview = true;
        try
        {
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
        }
        finally
        {
            _suspendPreview = false;
        }

        _ = LoadInfoAsync();
    }

    partial void OnBumpChanged(VersionBump value) => RefreshPreview();
    partial void OnUseManualVersionChanged(bool value) => RefreshPreview();
    partial void OnManualVersionChanged(string value) => RefreshPreview();

    private void RefreshPreview()
    {
        if (!_suspendPreview)
            _ = UpdatePreviewAsync();
    }

    private async Task LoadInfoAsync()
    {
        var project = Project;
        if (project is null)
            return;

        try
        {
            if (!await _git.IsGitRepositoryAsync(project.LocalPath))
            {
                if (IsStillCurrent(project))
                    CurrentVersion = "(非 git 儲存庫)";
                return;
            }

            var branch = await _git.GetCurrentBranchAsync(project.LocalPath);
            var tags = await _git.ListTagsAsync(project.LocalPath);

            // The user may have switched projects while we were awaiting git;
            // don't overwrite the newly selected project's info with stale data.
            if (!IsStillCurrent(project))
                return;

            Branch = branch;
            var latest = _semver.GetLatest(tags, project.TagPrefix);
            CurrentVersion = latest is null ? "(尚無 tag)" : _semver.ToTag(latest, project.TagPrefix);
            NextVersionPreview = BuildPreview(project, tags);
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
                // A manual version needs no git round-trip.
                NextVersionPreview = BuildPreview(project, []);
                return;
            }

            var tags = await _git.ListTagsAsync(project.LocalPath);
            if (IsStillCurrent(project))
                NextVersionPreview = BuildPreview(project, tags);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Preview update failed.");
        }
    }

    private bool IsStillCurrent(Project project) => ReferenceEquals(Project, project);

    private string? BuildPreview(Project project, IReadOnlyList<string> tags)
    {
        if (!UseManualVersion)
            return _semver.ComputeNextFromTags(tags, Bump, project.TagPrefix).NextTag;

        var version = _semver.ParseVersion(ManualVersion, project.TagPrefix);
        return version is null ? "(版號格式不正確)" : _semver.ToTag(version, project.TagPrefix);
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

    /// <summary>
    /// Appends a step-output line, keeping only the tail — a chatty build can
    /// emit tens of thousands of lines, and the bound TextBox is not virtualized.
    /// </summary>
    private void OnLogLine(ProcessLine line)
    {
        var combined = LogText + (line.IsError ? "[err] " : string.Empty) + line.Text + "\n";

        if (combined.Length > MaxLogChars)
        {
            var start = combined.Length - MaxLogChars;
            var newline = combined.IndexOf('\n', start);
            combined = "…(較早的輸出已截斷)…\n" + combined[(newline >= 0 ? newline + 1 : start)..];
        }

        LogText = combined;
    }

    private const int MaxLogChars = 80_000;
}
