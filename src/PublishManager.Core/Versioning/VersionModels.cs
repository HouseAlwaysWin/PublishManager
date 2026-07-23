namespace PublishManager.Core.Versioning;

/// <summary>How to increment a version.</summary>
public enum VersionBump
{
    Major,
    Minor,
    Patch,
    Prerelease,
}

/// <summary>
/// The outcome of computing the next version from a repo's existing tags:
/// the current (latest) tag if any, and the proposed next tag. Both are
/// prefixed tag strings so the UI never has to touch the SemVer type.
/// </summary>
public sealed record NextVersionResult(string? CurrentTag, string NextTag)
{
    public bool HadPrevious => CurrentTag is not null;
}
