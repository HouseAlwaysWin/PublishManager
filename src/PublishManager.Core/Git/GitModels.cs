namespace PublishManager.Core.Git;

/// <summary>An owner/repository pair parsed from a git remote URL.</summary>
public readonly record struct RepoSlug(string Owner, string Repo)
{
    public override string ToString() => $"{Owner}/{Repo}";
}

/// <summary>A tag with the commit it points at and when that commit was authored.</summary>
public sealed record TagInfo(string Name, string CommitSha, DateTimeOffset? Date);

/// <summary>Thrown when a git command fails (non-zero exit).</summary>
public sealed class GitException(string message) : Exception(message);
