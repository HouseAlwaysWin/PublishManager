using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PublishManager.Views;

public partial class TagManagerWindow : Window
{
    public TagManagerWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
