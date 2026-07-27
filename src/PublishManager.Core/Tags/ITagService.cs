using PublishManager.Core.Models;

namespace PublishManager.Core.Tags;

/// <summary>
/// Lists and deletes a project's version tags. Deletion is destructive and
/// scoped by <see cref="TagDeletionOptions"/> — local, remote, and the published
/// GitHub Release are each opt-in.
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Lists the project's tags, newest version first, annotated with where each
    /// exists (local / remote / GitHub Release).
    /// </summary>
    Task<IReadOnlyList<VersionTag>> ListAsync(Project project, CancellationToken ct = default);

    /// <summary>Deletes one tag according to <paramref name="options"/>.</summary>
    Task<TagDeletionResult> DeleteAsync(
        Project project,
        string tag,
        TagDeletionOptions options,
        CancellationToken ct = default);
}
