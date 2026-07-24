using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PublishManager.Core.Detection;
using PublishManager.Core.Models;
using PublishManager.ViewModels;
using PublishManager.Views;

namespace PublishManager.Services;

/// <summary>Default <see cref="IDialogService"/> using Avalonia windows.</summary>
public sealed class DialogService(IProjectDetector detector) : IDialogService
{
    private readonly IProjectDetector _detector = detector;

    public async Task<Project?> ShowProjectEditorAsync(Project? existing)
    {
        var window = new ProjectEditorWindow
        {
            DataContext = new ProjectEditorViewModel(existing, _detector),
        };

        var owner = MainWindow;
        return owner is not null
            ? await window.ShowDialog<Project?>(owner)
            : null;
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
