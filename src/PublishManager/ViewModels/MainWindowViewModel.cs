using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Models;
using PublishManager.Core.Storage;
using PublishManager.Services;

namespace PublishManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IProjectStore _store;
    private readonly IDialogService _dialogs;
    private readonly Func<ReleaseViewModel> _releaseFactory;
    private readonly ILogger<MainWindowViewModel> _logger;

    /// <summary>One release panel per project, kept alive across selection changes.</summary>
    private readonly Dictionary<Guid, ReleaseViewModel> _releases = [];

    public MainWindowViewModel(
        IProjectStore store,
        IDialogService dialogs,
        Func<ReleaseViewModel> releaseFactory,
        ILogger<MainWindowViewModel> logger)
    {
        _store = store;
        _dialogs = dialogs;
        _releaseFactory = releaseFactory;
        _logger = logger;
    }

    public ObservableCollection<Project> Projects { get; } = [];

    /// <summary>
    /// Release panel for the selected project. Each project keeps its own, so a
    /// release (and its Actions monitor) started on one project keeps running —
    /// and is still on screen — after switching away and back.
    /// </summary>
    [ObservableProperty] private ReleaseViewModel? _release;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManageTagsCommand))]
    private Project? _selectedProject;

    public bool HasSelection => SelectedProject is not null;

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (value is null)
        {
            Release = null;
            return;
        }

        if (_releases.TryGetValue(value.Id, out var existing))
        {
            // Same project (possibly an edited copy) — keep its stages/monitor.
            existing.UpdateProject(value);
            Release = existing;
            return;
        }

        var created = _releaseFactory();
        created.Initialize(value);
        _releases[value.Id] = created;
        Release = created;
    }

    /// <summary>Loads persisted projects. Call once after the window opens.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var loaded = await _store.LoadAsync();
            Projects.Clear();
            foreach (var project in loaded)
                Projects.Add(project);
            SelectedProject = Projects.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load projects on startup.");
        }
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        var created = await _dialogs.ShowProjectEditorAsync(null);
        if (created is null)
            return;

        Projects.Add(created);
        SelectedProject = created;
        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditProjectAsync()
    {
        if (SelectedProject is null)
            return;

        var edited = await _dialogs.ShowProjectEditorAsync(SelectedProject);
        if (edited is null)
            return;

        var index = Projects.IndexOf(SelectedProject);
        if (index >= 0)
            Projects[index] = edited;
        SelectedProject = edited;
        await SaveAsync();
    }

    /// <summary>Opens the tag/version manager for the selected project.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ManageTagsAsync()
    {
        if (SelectedProject is null)
            return;

        await _dialogs.ShowTagManagerAsync(SelectedProject);

        // Tags may have been deleted — refresh the release panel's version info.
        if (Release is not null)
            Release.UpdateProject(SelectedProject);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveProjectAsync()
    {
        if (SelectedProject is null)
            return;

        if (_releases.Remove(SelectedProject.Id, out var release))
            release.Shutdown();

        Projects.Remove(SelectedProject);
        SelectedProject = Projects.FirstOrDefault();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync([.. Projects]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save projects.");
        }
    }
}
