namespace PublishManager.Core.Models;

/// <summary>Rules governing the set of managed projects as a whole.</summary>
public static class ProjectRules
{
    /// <summary>
    /// Returns a message describing the project <paramref name="candidate"/>
    /// would collide with, or null when it is free to be added.
    ///
    /// A repository may host several projects — that is how a monorepo keeps
    /// separate release lines — but two sharing both a path and a tag prefix
    /// would manage the very same tags, so each would show the other's
    /// deletions as stale.
    /// </summary>
    public static string? FindConflict(IEnumerable<Project> existing, Project candidate)
    {
        var path = NormalisePath(candidate.LocalPath);
        var prefix = candidate.TagPrefix ?? string.Empty;

        var clash = existing.FirstOrDefault(p =>
            p.Id != candidate.Id &&
            string.Equals(NormalisePath(p.LocalPath), path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.TagPrefix ?? string.Empty, prefix, StringComparison.Ordinal));

        return clash is null
            ? null
            : $"「{clash.Name}」已經在管理這個路徑的 '{prefix}' 版本線。" +
              "同一個 repo 可以有多個專案,但 tag 前綴必須不同。";
    }

    /// <summary>Trims whitespace and any trailing separator so equal folders compare equal.</summary>
    private static string NormalisePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
