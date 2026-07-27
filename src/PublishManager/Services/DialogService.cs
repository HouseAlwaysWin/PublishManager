using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PublishManager.Core.Detection;
using PublishManager.Core.Models;
using PublishManager.ViewModels;
using PublishManager.Views;

namespace PublishManager.Services;

/// <summary>Default <see cref="IDialogService"/> using Avalonia windows.</summary>
public sealed class DialogService(
    IProjectDetector detector,
    Func<TagManagerViewModel> tagManagerFactory,
    Func<ReleaseLedgerViewModel> ledgerFactory) : IDialogService
{
    private readonly IProjectDetector _detector = detector;
    private readonly Func<TagManagerViewModel> _tagManagerFactory = tagManagerFactory;
    private readonly Func<ReleaseLedgerViewModel> _ledgerFactory = ledgerFactory;

    public async Task<Project?> ShowProjectEditorAsync(Project? existing, IReadOnlyList<Project> allProjects)
    {
        var window = new ProjectEditorWindow
        {
            DataContext = new ProjectEditorViewModel(existing, _detector, allProjects),
        };

        var owner = MainWindow;
        return owner is not null
            ? await window.ShowDialog<Project?>(owner)
            : null;
    }

    public async Task ShowTagManagerAsync(Project project)
    {
        var viewModel = _tagManagerFactory();
        viewModel.Initialize(project);

        var window = new TagManagerWindow { DataContext = viewModel };

        var owner = MainWindow;
        if (owner is not null)
            await window.ShowDialog(owner);
    }

    public async Task ShowReleaseLedgerAsync(Project project)
    {
        var viewModel = _ledgerFactory();
        viewModel.Initialize(project);

        var window = new ReleaseLedgerWindow { DataContext = viewModel };

        var owner = MainWindow;
        if (owner is not null)
            await window.ShowDialog(owner);
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
