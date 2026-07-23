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
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        IProjectStore store,
        IDialogService dialogs,
        ReleaseViewModel release,
        ILogger<MainWindowViewModel> logger)
    {
        _store = store;
        _dialogs = dialogs;
        Release = release;
        _logger = logger;
    }

    public ObservableCollection<Project> Projects { get; } = [];

    /// <summary>Release panel bound to the currently selected project.</summary>
    public ReleaseViewModel Release { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveProjectCommand))]
    private Project? _selectedProject;

    public bool HasSelection => SelectedProject is not null;

    partial void OnSelectedProjectChanged(Project? value) => Release.SetProject(value);

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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveProjectAsync()
    {
        if (SelectedProject is null)
            return;

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
