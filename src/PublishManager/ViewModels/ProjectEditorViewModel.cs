using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PublishManager.Core.Detection;
using PublishManager.Core.Models;
using PublishManager.Core.Versioning;

namespace PublishManager.ViewModels;

/// <summary>Backing view model for the add/edit project dialog.</summary>
public partial class ProjectEditorViewModel : ViewModelBase
{
    private readonly Guid _id;
    private readonly Dictionary<string, string> _dispatchInputs;
    private readonly IProjectDetector? _detector;

    public ProjectEditorViewModel() : this(null, null) { }

    public ProjectEditorViewModel(Project? existing, IProjectDetector? detector = null)
    {
        _detector = detector;
        var p = existing ?? new Project();
        _id = p.Id;
        _dispatchInputs = new Dictionary<string, string>(p.DispatchInputs);

        _name = p.Name;
        _localPath = p.LocalPath;
        _kind = p.Kind;
        _releaseModel = p.ReleaseModel;
        _tagPrefix = p.TagPrefix;
        _defaultBump = p.DefaultBump;
        _releaseBranch = p.ReleaseBranch ?? string.Empty;
        _owner = p.Owner ?? string.Empty;
        _repo = p.Repo ?? string.Empty;
        _workflowFile = p.WorkflowFile ?? string.Empty;
        _stampVersionIntoBuild = p.StampVersionIntoBuild;

        Steps = new ObservableCollection<ReleaseStepViewModel>(
            p.Steps.Select(s => new ReleaseStepViewModel(s)));

        Title = existing is null ? "新增專案" : "編輯專案";
    }

    public string Title { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _localPath;
    [ObservableProperty] private ProjectKind _kind;
    [ObservableProperty] private ReleaseModel _releaseModel;
    [ObservableProperty] private string _tagPrefix;
    [ObservableProperty] private VersionBump _defaultBump;
    [ObservableProperty] private string _releaseBranch;
    [ObservableProperty] private string _owner;
    [ObservableProperty] private string _repo;
    [ObservableProperty] private string _workflowFile;
    [ObservableProperty] private bool _stampVersionIntoBuild;
    [ObservableProperty] private ReleaseStepViewModel? _selectedStep;
    [ObservableProperty] private string? _error;

    public ObservableCollection<ReleaseStepViewModel> Steps { get; }

    public ProjectKind[] Kinds { get; } = Enum.GetValues<ProjectKind>();
    public ReleaseModel[] ReleaseModels { get; } = Enum.GetValues<ReleaseModel>();
    public VersionBump[] Bumps { get; } = Enum.GetValues<VersionBump>();

    [RelayCommand]
    private void AddStep()
    {
        var step = new ReleaseStepViewModel(new ReleaseStep { Name = "new-step" });
        Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedStep is null)
            return;
        Steps.Remove(SelectedStep);
        SelectedStep = Steps.LastOrDefault();
    }

    [ObservableProperty] private bool _isDetecting;
    [ObservableProperty] private string? _detectSummary;

    /// <summary>
    /// Infers settings from the local folder: project kind, owner/repo (git
    /// remote), tag prefix (from existing tags), and the workflow file plus the
    /// release model its triggers imply.
    /// </summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        if (_detector is null || string.IsNullOrWhiteSpace(LocalPath))
            return;

        IsDetecting = true;
        Error = null;
        try
        {
            var detection = await _detector.DetectAsync(LocalPath.Trim());

            SuggestNameFromPathIfEmpty();
            Kind = detection.Kind;

            if (detection.Slug is { } slug)
            {
                Owner = slug.Owner;
                Repo = slug.Repo;
            }
            if (!string.IsNullOrEmpty(detection.TagPrefix))
                TagPrefix = detection.TagPrefix;
            if (!string.IsNullOrEmpty(detection.SuggestedWorkflowFile))
                WorkflowFile = detection.SuggestedWorkflowFile;
            if (detection.SuggestedReleaseModel is { } model)
                ReleaseModel = model;

            DetectSummary = BuildDetectSummary(detection);
        }
        catch (Exception ex)
        {
            DetectSummary = $"偵測失敗:{ex.Message}";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private static string BuildDetectSummary(ProjectDetection detection)
    {
        var parts = new List<string>
        {
            detection.IsGitRepository ? "git ✓" : "非 git 儲存庫",
            detection.Kind.ToString(),
        };

        if (detection.Slug is { } slug)
            parts.Add($"{slug.Owner}/{slug.Repo}");
        if (!string.IsNullOrEmpty(detection.CurrentBranch))
            parts.Add($"分支 {detection.CurrentBranch}");

        if (!string.IsNullOrEmpty(detection.SuggestedWorkflowFile))
            parts.Add($"workflow {detection.SuggestedWorkflowFile}");
        else if (detection.Workflows.Count == 0)
            parts.Add("找不到 workflow");

        return "偵測結果:" + string.Join("  ·  ", parts);
    }

    /// <summary>If the name is still blank, derive it from the local path's folder name.</summary>
    public void SuggestNameFromPathIfEmpty()
    {
        if (!string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(LocalPath))
            return;

        try
        {
            var folder = new DirectoryInfo(LocalPath.Trim()).Name;
            if (!string.IsNullOrEmpty(folder))
                Name = folder;
        }
        catch
        {
            // ignore malformed paths
        }
    }

    public bool Validate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(LocalPath))
        {
            error = "請選擇專案的本機路徑。";
            return false;
        }
        if (!Directory.Exists(LocalPath))
        {
            error = "本機路徑不存在。";
            return false;
        }

        // Fall back to the folder name when the user left the name blank.
        SuggestNameFromPathIfEmpty();

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "請輸入專案名稱。";
            return false;
        }
        error = null;
        return true;
    }

    public Project BuildProject() => new()
    {
        Id = _id,
        Name = Name.Trim(),
        LocalPath = LocalPath.Trim(),
        Kind = Kind,
        ReleaseModel = ReleaseModel,
        TagPrefix = string.IsNullOrWhiteSpace(TagPrefix) ? "v" : TagPrefix.Trim(),
        DefaultBump = DefaultBump,
        ReleaseBranch = string.IsNullOrWhiteSpace(ReleaseBranch) ? null : ReleaseBranch.Trim(),
        Owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim(),
        Repo = string.IsNullOrWhiteSpace(Repo) ? null : Repo.Trim(),
        WorkflowFile = string.IsNullOrWhiteSpace(WorkflowFile) ? null : WorkflowFile.Trim(),
        StampVersionIntoBuild = StampVersionIntoBuild,
        Steps = [.. Steps.Select(s => s.ToModel())],
        DispatchInputs = _dispatchInputs,
    };
}
