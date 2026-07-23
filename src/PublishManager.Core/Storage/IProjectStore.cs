using PublishManager.Core.Models;

namespace PublishManager.Core.Storage;

/// <summary>Persists the managed-project list. Load-all / save-all is ample for this scale.</summary>
public interface IProjectStore
{
    /// <summary>Loads all projects; returns an empty list if nothing has been saved yet.</summary>
    Task<IReadOnlyList<Project>> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the full project list atomically (temp file + replace).</summary>
    Task SaveAsync(IReadOnlyList<Project> projects, CancellationToken ct = default);
}
