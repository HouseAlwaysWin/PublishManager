using Avalonia.Media;

namespace PublishManager.ViewModels;

/// <summary>What kind of thing a release source suggestion names.</summary>
public enum ReleaseSourceKind
{
    Branch,
    Tag,
    Commit,
}

/// <summary>
/// One suggestion in the release-source box.
/// <see cref="SearchText"/> is what the box filters on and what lands in the
/// text field when the item is chosen — for a commit it carries the subject
/// after the sha, so the commit can be found by what it says. The orchestrator
/// keeps only the leading token, which is safe because no rev contains a space.
/// </summary>
public sealed class ReleaseSourceOption
{
    public required string Rev { get; init; }

    public required ReleaseSourceKind Kind { get; init; }

    /// <summary>Commit subject, or the empty string for branches and tags.</summary>
    public string Detail { get; init; } = string.Empty;

    public string? Date { get; init; }

    public string SearchText => Detail.Length == 0 ? Rev : $"{Rev} {Detail}";

    public string KindLabel => Kind switch
    {
        ReleaseSourceKind.Branch => "分支",
        ReleaseSourceKind.Tag => "Tag",
        _ => "Commit",
    };

    public IBrush KindBrush => Kind switch
    {
        ReleaseSourceKind.Branch => RunGlyph.Running,
        ReleaseSourceKind.Tag => RunGlyph.Success,
        _ => RunGlyph.Pending,
    };

    public bool HasDetail => Detail.Length > 0;

    public static ReleaseSourceOption ForBranch(string name) =>
        new() { Rev = name, Kind = ReleaseSourceKind.Branch };

    public static ReleaseSourceOption ForTag(string name) =>
        new() { Rev = name, Kind = ReleaseSourceKind.Tag };

    public static ReleaseSourceOption ForCommit(string shortSha, string subject, DateTimeOffset? date) =>
        new()
        {
            Rev = shortSha,
            Kind = ReleaseSourceKind.Commit,
            Detail = subject,
            Date = date?.LocalDateTime.ToString("yyyy-MM-dd"),
        };
}
