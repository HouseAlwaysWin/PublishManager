using PublishManager.Core.Models;

namespace PublishManager.Services;

/// <summary>Shows modal dialogs on behalf of view models (keeps VMs free of window plumbing).</summary>
public interface IDialogService
{
    /// <summary>
    /// Opens the project editor. Returns the built project on save, or null if cancelled.
    /// Pass <paramref name="existing"/> to edit (the result keeps the same Id), or null to add.
    /// </summary>
    Task<Project?> ShowProjectEditorAsync(Project? existing);
}
