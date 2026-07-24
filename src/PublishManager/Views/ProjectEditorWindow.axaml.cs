using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PublishManager.ViewModels;

namespace PublishManager.Views;

public partial class ProjectEditorWindow : Window
{
    public ProjectEditorWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectEditorViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選擇專案的本機路徑",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                vm.LocalPath = path;
                vm.SuggestNameFromPathIfEmpty();
                // Fill in kind / owner / repo / tag prefix / workflow straight away.
                await vm.DetectCommand.ExecuteAsync(null);
            }
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectEditorViewModel vm)
            return;

        if (!vm.Validate(out var error))
        {
            vm.Error = error;
            return;
        }

        Close(vm.BuildProject());
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
