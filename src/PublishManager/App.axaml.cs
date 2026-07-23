using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PublishManager.Core;
using PublishManager.ViewModels;
using PublishManager.Views;

namespace PublishManager;

public partial class App : Application
{
    /// <summary>Application-wide service provider (composition root).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPublishManagerCore();
        builder.Services.AddViewModels();

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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
