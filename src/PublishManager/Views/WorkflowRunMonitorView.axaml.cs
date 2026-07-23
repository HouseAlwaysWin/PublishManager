using Avalonia.Controls;
using Avalonia.Interactivity;
using PublishManager.ViewModels;

namespace PublishManager.Views;

public partial class WorkflowRunMonitorView : UserControl
{
    public WorkflowRunMonitorView()
    {
        InitializeComponent();
    }

    private async void OnOpenRunClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkflowRunMonitorViewModel vm || string.IsNullOrWhiteSpace(vm.HtmlUrl))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null && Uri.TryCreate(vm.HtmlUrl, UriKind.Absolute, out var uri))
            await topLevel.Launcher.LaunchUriAsync(uri);
    }
}
