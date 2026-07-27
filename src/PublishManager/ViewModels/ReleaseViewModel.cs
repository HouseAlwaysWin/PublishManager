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

/// <summary>One row in the release progress list — a stage or a step.</summary>
public partial class ReleaseProgressRowViewModel(string key, string label) : ViewModelBase
{
    /// <summary>Stable identity of the row; never the display label.</summary>
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private ReleaseProgressStatus _status;

    [ObservableProperty] private string? _message;

    public string Glyph => Status switch
    {
        ReleaseProgressStatus.Succeeded => "✓",
        ReleaseProgressStatus.Failed => "✗",
        ReleaseProgressStatus.Running => "●",
        ReleaseProgressStatus.Skipped => "–",
        _ => "○",
    };

    public IBrush GlyphBrush => Status switch
    {
        ReleaseProgressStatus.Succeeded => RunGlyph.Success,
        ReleaseProgressStatus.Failed => RunGlyph.Failure,
        ReleaseProgressStatus.Running => RunGlyph.Running,
        ReleaseProgressStatus.Skipped => RunGlyph.Muted,
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

    public ObservableCollection<ReleaseProgressRowViewModel> Stages { get; } = [];

    /// <summary>Branches, tags, and recent commits offered as release sources.</summary>
    public ObservableCollection<ReleaseSourceOption> SourceSuggestions { get; } = [];
    public VersionBump[] Bumps { get; } = Enum.GetValues<VersionBump>();

    [ObservableProperty] private Project? _project;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ReleaseCommand))] private bool _hasProject;
    [ObservableProperty] private string? _currentVersion;
    [ObservableProperty] private string? _branch;
    [ObservableProperty] private VersionBump _bump;
    [ObservableProperty] private string? _nextVersionPreview;
    /// <summary>
    /// Branch, tag, or commit to release from. Empty means whatever is checked
    /// out; naming one never changes the working copy.
    /// </summary>
    [ObservableProperty] private string _source = string.Empty;

    [ObservableProperty] private bool _useManualVersion;
    [ObservableProperty] private string _manualVersion = string.Empty;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ReleaseCommand))] private bool _isReleasing;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _logText = string.Empty;

    /// <summary>Reconfigures the panel for a newly selected project (or clears it when null).</summary>
    /// <summary>Binds this panel to a project. Called once, when it is created for that project.</summary>
    public void Initialize(Project project)
    {
        // Setting these would otherwise fire several redundant, git-backed
        // preview refreshes; LoadInfoAsync does the work once.
        _suspendPreview = true;
        try
        {
            Project = project;
            HasProject = true;
            Bump = project.DefaultBump;
        }
        finally
        {
            _suspendPreview = false;
        }

        _ = LoadInfoAsync();
    }

    /// <summary>
    /// Re-points at an edited copy of the same project. Deliberately keeps the
    /// pipeline stages, log and any in-flight run monitor — they belong to this
    /// project and must survive switching away and back.
    /// </summary>
    public void UpdateProject(Project project)
    {
        _suspendPreview = true;
        try
        {
            Project = project;
            HasProject = true;
        }
        finally
        {
            _suspendPreview = false;
        }

        _ = LoadInfoAsync();
    }

    /// <summary>Cancels any in-flight release and stops monitoring (project removed / closing).</summary>
    public void Shutdown()
    {
        CancelInternal();
        Monitor.Stop();
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
            var branches = await _git.ListBranchesAsync(project.LocalPath);
            var commits = await _git.ListRecentCommitsAsync(project.LocalPath);

            // The user may have switched projects while we were awaiting git;
            // don't overwrite the newly selected project's info with stale data.
            if (!IsStillCurrent(project))
                return;

            Branch = branch;

            // Branches first — the common case — then tags newest version first,
            // then recent commits so one can be picked by what it says.
            SourceSuggestions.Clear();
            foreach (var name in branches)
                SourceSuggestions.Add(ReleaseSourceOption.ForBranch(name));
            foreach (var tag in OrderTagsNewestFirst(tags, project.TagPrefix))
                SourceSuggestions.Add(ReleaseSourceOption.ForTag(tag));
            foreach (var commit in commits)
                SourceSuggestions.Add(ReleaseSourceOption.ForCommit(commit.ShortSha, commit.Subject, commit.Date));
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

    /// <summary>Version tags newest first; anything unparseable trails behind by name.</summary>
    private IEnumerable<string> OrderTagsNewestFirst(IReadOnlyList<string> tags, string prefix) =>
        tags.Select(t => (Tag: t, Version: _semver.TryParseTag(t, prefix)))
            .OrderByDescending(x => x.Version is not null)
            .ThenByDescending(x => x.Version, SemVersionOrder.Instance)
            .ThenByDescending(x => x.Tag, StringComparer.Ordinal)
            .Select(x => x.Tag);

    private sealed class SemVersionOrder : IComparer<Semver.SemVersion?>
    {
        public static readonly SemVersionOrder Instance = new();

        public int Compare(Semver.SemVersion? x, Semver.SemVersion? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => x.CompareSortOrderTo(y),
        };
    }

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
        Monitor.Reset();   // clear the previous run before this project's new one
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
                Source = Source,
                DryRun = DryRun,
            };
            var result = await _orchestrator.RunAsync(request, events, log, _cts.Token);

            StatusMessage = result.Success
                ? result.DryRun
                    ? $"Dry-run 通過:發版會成功,下一版為 {result.Tag}(未推送 / 未觸發)"
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
        var stage = Stages.FirstOrDefault(s => s.Key == e.Key);
        if (stage is null)
        {
            stage = new ReleaseProgressRowViewModel(e.Key, e.Label);
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
