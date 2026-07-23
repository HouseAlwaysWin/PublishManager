using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PublishManager.Core.Models;
using PublishManager.ViewModels;
using PublishManager.Views;

namespace PublishManager.Services;

/// <summary>Default <see cref="IDialogService"/> using Avalonia windows.</summary>
public sealed class DialogService : IDialogService
{
    public async Task<Project?> ShowProjectEditorAsync(Project? existing)
    {
        var window = new ProjectEditorWindow
        {
            DataContext = new ProjectEditorViewModel(existing),
        };

        var owner = MainWindow;
        return owner is not null
            ? await window.ShowDialog<Project?>(owner)
            : null;
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
