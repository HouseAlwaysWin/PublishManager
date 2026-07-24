using Microsoft.Extensions.DependencyInjection;
using PublishManager.Core.Detection;
using PublishManager.Core.Git;
using PublishManager.Core.GitHub;
using PublishManager.Core.Processes;
using PublishManager.Core.Release;
using PublishManager.Core.Storage;
using PublishManager.Core.Versioning;

namespace PublishManager.Core;

/// <summary>
/// Registers all <c>PublishManager.Core</c> services into the DI container.
/// The UI composition root (App.axaml.cs) calls this once at startup.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPublishManagerCore(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<ISemVerService, SemVerService>();
        services.AddSingleton<IProjectDetector, ProjectDetector>();

        services.AddSingleton(StorageOptions.Default);
        services.AddSingleton<IProjectStore, JsonProjectStore>();

        services.AddSingleton<IGitHubAuthProvider, GitHubAuthProvider>();
        services.AddSingleton<IGitHubActionsService, GitHubActionsService>();

        services.AddSingleton<IReleaseOrchestrator, ReleaseOrchestrator>();
        return services;
    }
}
