using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PublishManager.Core;
using PublishManager.Core.Diagnostics;
using PublishManager.Core.Storage;
using PublishManager.Services;
using PublishManager.ViewModels;
using PublishManager.Views;

namespace PublishManager;

public partial class App : Application
{
    /// <summary>Application-wide service provider (composition root).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    private TrayIconController? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPublishManagerCore();
        builder.Services.AddViewModels();

        // This is a WinExe with no console attached, so the default providers
        // write nowhere. Without a file sink, every logged failure is invisible.
        builder.Logging.AddProvider(new FileLoggerProvider(StorageOptions.Default.LogDirectory));

        var host = builder.Build();
        Services = host.Services;

        // Note (Avalonia 12): the CommunityToolkit data-annotations validation
        // plugin is disabled by default, so the old 11-era
        // `BindingPlugins.DataValidators.RemoveAt(0)` workaround is not needed.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = viewModel };
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
            desktop.MainWindow = window;

            // Closing hides to the notification area; the tray menu is the way out.
            _tray = new TrayIconController(desktop, window);
            desktop.Exit += (_, _) => _tray?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
