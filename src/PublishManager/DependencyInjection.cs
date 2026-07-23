using Microsoft.Extensions.DependencyInjection;
using PublishManager.Services;
using PublishManager.ViewModels;

namespace PublishManager;

/// <summary>Registers the UI-layer services and view models into the DI container.</summary>
public static class ViewModelServiceCollectionExtensions
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<WorkflowRunMonitorViewModel>();
        services.AddSingleton<ReleaseViewModel>();
        services.AddTransient<MainWindowViewModel>();
        return services;
    }
}
