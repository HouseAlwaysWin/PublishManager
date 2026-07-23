using Semver;

namespace PublishManager.Core.Versioning;

/// <summary>
/// SemVer 2.0 tag math (git tags are the version source of truth). Parses tags
/// honoring a configurable prefix (e.g. "v"), picks the latest by SemVer sort
/// order, and computes the next version for a given bump type. Pure and
/// deterministic — heavily unit-tested.
/// </summary>
public interface ISemVerService
{
    /// <summary>
    /// Parses a tag (stripping <paramref name="prefix"/> first) into a
    /// <see cref="SemVersion"/>, or null if it is not a valid version tag.
    /// </summary>
    SemVersion? TryParseTag(string tag, string prefix = "v");

    /// <summary>Returns the highest version among <paramref name="tags"/> by sort order, or null if none parse.</summary>
    SemVersion? GetLatest(IEnumerable<string> tags, string prefix = "v");

    /// <summary>Computes the next version from <paramref name="current"/> for the given bump.</summary>
    SemVersion ComputeNext(SemVersion current, VersionBump bump, string? prereleaseLabel = null);

    /// <summary>Formats a version back into a prefixed tag string.</summary>
    string ToTag(SemVersion version, string prefix = "v");

    /// <summary>
    /// Leniently parses a user-entered version for a manual release. The
    /// <paramref name="prefix"/> is optional in the input ("2.0.0" and "v2.0.0"
    /// both parse). Returns null if the value is not valid SemVer.
    /// </summary>
    SemVersion? ParseVersion(string input, string prefix = "v");

    /// <summary>
    /// Convenience for the UI/orchestrator: from a repo's existing tags, produce
    /// the current tag (if any) and the proposed next tag as strings. When no
    /// tags exist yet, the base version is 0.0.0 (so a first patch → 0.0.1,
    /// minor → 0.1.0, major → 1.0.0).
    /// </summary>
    NextVersionResult ComputeNextFromTags(
        IEnumerable<string> tags,
        VersionBump bump,
        string prefix = "v",
        string? prereleaseLabel = null);
}
