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

        services.AddTransient<TagManagerViewModel>();
        services.AddSingleton<Func<TagManagerViewModel>>(sp => sp.GetRequiredService<TagManagerViewModel>);

        services.AddTransient<ReleaseLedgerViewModel>();
        services.AddSingleton<Func<ReleaseLedgerViewModel>>(sp => sp.GetRequiredService<ReleaseLedgerViewModel>);

        // One release panel (and monitor) per project — created on demand.
        services.AddTransient<ReleaseViewModel>();
        services.AddSingleton<Func<ReleaseViewModel>>(sp => sp.GetRequiredService<ReleaseViewModel>);

        services.AddTransient<MainWindowViewModel>();
        return services;
    }
}
