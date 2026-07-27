using PublishManager.Core.Models;

namespace PublishManager.Services;

/// <summary>Shows modal dialogs on behalf of view models (keeps VMs free of window plumbing).</summary>
public interface IDialogService
{
    /// <summary>
    /// Opens the project editor. Returns the built project on save, or null if cancelled.
    /// Pass <paramref name="existing"/> to edit (the result keeps the same Id), or null to add.
    /// </summary>
    /// <param name="allProjects">
    /// Every managed project, so the editor can refuse a path/tag-prefix pair
    /// another project already owns.
    /// </param>
    Task<Project?> ShowProjectEditorAsync(Project? existing, IReadOnlyList<Project> allProjects);

    /// <summary>Opens the tag manager for <paramref name="project"/>'s live tags.</summary>
    Task ShowTagManagerAsync(Project project);

    /// <summary>Opens the release ledger recorded for <paramref name="project"/>.</summary>
    Task ShowReleaseLedgerAsync(Project project);
}
