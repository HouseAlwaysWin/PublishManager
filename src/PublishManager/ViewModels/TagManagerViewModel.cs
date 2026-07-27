using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Models;
using PublishManager.Core.Tags;

namespace PublishManager.ViewModels;

/// <summary>One row in the tag list, selectable for deletion.</summary>
public partial class TagRowViewModel(VersionTag tag) : ViewModelBase
{
    public VersionTag Tag { get; } = tag;

    public string Name => Tag.Name;
    public string ShortSha => Tag.ShortSha;
    public string DateText => Tag.Date?.LocalDateTime.ToString("yyyy-MM-dd") ?? string.Empty;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Compact "where it exists" summary, e.g. "本機 · 遠端 · Release".</summary>
    public string Locations
    {
        get
        {
            var parts = new List<string>(3);
            if (Tag.ExistsLocally) parts.Add("本機");
            if (Tag.ExistsOnRemote) parts.Add("遠端");
            if (Tag.HasGitHubRelease) parts.Add("Release");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public IBrush LocationBrush => Tag.HasGitHubRelease ? RunGlyph.Warning : RunGlyph.Pending;

    public bool IsInvalidVersion => Tag.Version is null;
}

/// <summary>Backing view model for the tag/version management dialog.</summary>
public partial class TagManagerViewModel : ViewModelBase
{
    private readonly ITagService _tags;
    private readonly ILogger<TagManagerViewModel> _logger;
    private Project? _project;

    public TagManagerViewModel(ITagService tags, ILogger<TagManagerViewModel> logger)
    {
        _tags = tags;
        _logger = logger;
    }

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    [ObservableProperty] private string _title = "Tag / 版本管理";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _deleteLocal = true;
    [ObservableProperty] private bool _deleteRemote = true;
    [ObservableProperty] private bool _deleteGitHubRelease = true;

    /// <summary>True once the user has confirmed a pending deletion prompt.</summary>
    [ObservableProperty] private bool _isConfirming;

    [ObservableProperty] private string? _confirmMessage;

    public int SelectedCount => Tags.Count(t => t.IsSelected);

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagRowViewModel.IsSelected))
            OnPropertyChanged(nameof(SelectedCount));
    }

    public void Initialize(Project project)
    {
        _project = project;
        Title = $"Tag / 版本管理 — {project.Name}";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_project is null)
            return;

        IsBusy = true;
        IsConfirming = false;
        Status = null;
        try
        {
            var list = await _tags.ListAsync(_project);
            foreach (var old in Tags)
                old.PropertyChanged -= OnRowPropertyChanged;
            Tags.Clear();
            foreach (var tag in list)
            {
                var row = new TagRowViewModel(tag);
                row.PropertyChanged += OnRowPropertyChanged;
                Tags.Add(row);
            }
            OnPropertyChanged(nameof(SelectedCount));

            if (Tags.Count == 0)
                Status = "這個專案還沒有任何 tag。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list tags.");
            Status = $"讀取 tag 失敗:{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Shows the confirmation prompt listing exactly what will be deleted.</summary>
    [RelayCommand]
    private void RequestDelete()
    {
        var selected = Tags.Where(t => t.IsSelected).Select(t => t.Name).ToList();
        if (selected.Count == 0)
        {
            Status = "請先勾選要刪除的 tag。";
            return;
        }

        var scopes = new List<string>(3);
        if (DeleteLocal) scopes.Add("本機");
        if (DeleteRemote) scopes.Add("遠端");
        if (DeleteGitHubRelease) scopes.Add("GitHub Release");

        if (scopes.Count == 0)
        {
            Status = "請至少選擇一個刪除範圍。";
            return;
        }

        var names = string.Join("、", selected.Take(5));
        if (selected.Count > 5)
            names += $" 等 {selected.Count} 個";

        ConfirmMessage = $"確定要刪除 {names}?將移除:{string.Join(" / ", scopes)}。此操作無法復原。";
        IsConfirming = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirming = false;
        ConfirmMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_project is null)
            return;

        var selected = Tags.Where(t => t.IsSelected).ToList();
        IsConfirming = false;
        IsBusy = true;

        var options = new TagDeletionOptions
        {
            DeleteLocal = DeleteLocal,
            DeleteRemote = DeleteRemote,
            DeleteGitHubRelease = DeleteGitHubRelease,
        };

        var failures = new List<string>();
        var deleted = 0;

        try
        {
            foreach (var row in selected)
            {
                var result = await _tags.DeleteAsync(_project, row.Name, options);
                if (result.Success)
                    deleted++;
                else
                    failures.Add($"{row.Name}({result.Error})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tag deletion failed.");
            failures.Add(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();

        Status = failures.Count == 0
            ? $"已刪除 {deleted} 個 tag。"
            : $"已刪除 {deleted} 個;失敗:{string.Join(" / ", failures)}";
    }
}
