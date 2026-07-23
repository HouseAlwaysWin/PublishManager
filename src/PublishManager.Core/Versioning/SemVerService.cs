using System.Numerics;
using Semver;

namespace PublishManager.Core.Versioning;

/// <summary>Default <see cref="ISemVerService"/> backed by the <c>Semver</c> 3.x library.</summary>
public sealed class SemVerService : ISemVerService
{
    private const string DefaultPrereleaseLabel = "rc";

    public SemVersion? TryParseTag(string tag, string prefix = "v")
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var s = tag.Trim();
        if (!string.IsNullOrEmpty(prefix))
        {
            if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            s = s[prefix.Length..];
        }

        return SemVersion.TryParse(s, SemVersionStyles.Strict, out var version) ? version : null;
    }

    public SemVersion? GetLatest(IEnumerable<string> tags, string prefix = "v")
    {
        SemVersion? latest = null;
        foreach (var tag in tags)
        {
            var version = TryParseTag(tag, prefix);
            if (version is null)
                continue;
            if (latest is null || version.CompareSortOrderTo(latest) > 0)
                latest = version;
        }
        return latest;
    }

    public SemVersion ComputeNext(SemVersion current, VersionBump bump, string? prereleaseLabel = null)
    {
        var label = string.IsNullOrWhiteSpace(prereleaseLabel) ? DefaultPrereleaseLabel : prereleaseLabel.Trim();

        return bump switch
        {
            VersionBump.Major => new SemVersion(current.Major + 1, 0, 0),
            VersionBump.Minor => new SemVersion(current.Major, current.Minor + 1, 0),
            VersionBump.Patch => new SemVersion(current.Major, current.Minor, current.Patch + 1),
            VersionBump.Prerelease => NextPrerelease(current, label),
            _ => throw new ArgumentOutOfRangeException(nameof(bump), bump, "Unknown version bump."),
        };
    }

    public string ToTag(SemVersion version, string prefix = "v") => prefix + version;

    public NextVersionResult ComputeNextFromTags(
        IEnumerable<string> tags,
        VersionBump bump,
        string prefix = "v",
        string? prereleaseLabel = null)
    {
        var latest = GetLatest(tags, prefix);
        var baseVersion = latest ?? new SemVersion(0, 0, 0);
        var next = ComputeNext(baseVersion, bump, prereleaseLabel);

        var currentTag = latest is null ? null : ToTag(latest, prefix);
        return new NextVersionResult(currentTag, ToTag(next, prefix));
    }

    private static SemVersion NextPrerelease(SemVersion current, string label)
    {
        // A stable version starts a new prerelease series on the next patch: 1.2.3 -> 1.2.4-rc.1
        if (!current.IsPrerelease)
            return SemVersion.ParsedFrom(current.Major, current.Minor, current.Patch + 1, $"{label}.1");

        // An existing prerelease increments its trailing numeric identifier, or gains ".1" if none.
        var parts = current.Prerelease.Split('.');
        var last = parts[^1];
        if (last.Length > 0 && last.All(char.IsDigit))
            parts[^1] = (BigInteger.Parse(last) + 1).ToString();
        else
            parts = [.. parts, "1"];

        return current.WithPrereleaseParsedFrom(string.Join('.', parts));
    }
}
