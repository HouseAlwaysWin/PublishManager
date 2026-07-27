namespace PublishManager.Core.Tags;

/// <summary>A version tag with everywhere it currently exists.</summary>
public sealed record VersionTag
{
    public required string Name { get; init; }

    /// <summary>Parsed version, or null when the tag is not valid SemVer.</summary>
    public string? Version { get; init; }

    public string? CommitSha { get; init; }
    public DateTimeOffset? Date { get; init; }

    public bool ExistsLocally { get; init; }
    public bool ExistsOnRemote { get; init; }

    /// <summary>True when a GitHub Release is published for this tag.</summary>
    public bool HasGitHubRelease { get; init; }

    public string ShortSha => CommitSha is { Length: > 7 } sha ? sha[..7] : CommitSha ?? string.Empty;
}

/// <summary>What to remove when deleting a tag.</summary>
public sealed record TagDeletionOptions
{
    public bool DeleteLocal { get; init; } = true;
    public bool DeleteRemote { get; init; } = true;

    /// <summary>Also delete the GitHub Release published for the tag, if any.</summary>
    public bool DeleteGitHubRelease { get; init; }
}

/// <summary>Outcome of deleting one tag.</summary>
public sealed record TagDeletionResult(
    string Tag,
    bool Success,
    bool DeletedLocal,
    bool DeletedRemote,
    bool DeletedRelease,
    string? Error);
