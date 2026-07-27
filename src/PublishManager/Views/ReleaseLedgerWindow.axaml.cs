using Avalonia.Controls;
using Avalonia.Interactivity;
using PublishManager.ViewModels;

namespace PublishManager.Views;

public partial class ReleaseLedgerWindow : Window
{
    public ReleaseLedgerWindow()
    {
        InitializeComponent();
    }

    private async void OnOpenRunClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LedgerEntryRowViewModel row })
            return;

        var url = row.Entry.RunUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await topLevel.Launcher.LaunchUriAsync(uri);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
