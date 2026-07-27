using System.Text.Json.Serialization;

namespace PublishManager.Core.Ledger;

/// <summary>
/// One release, as remembered by the Release Ledger. Holds the facts of the
/// release and deliberately not its logs — those stay on GitHub, and a job's
/// log runs to six figures of characters.
/// </summary>
public sealed record LedgerEntry
{
    public required Guid ProjectId { get; init; }

    /// <summary>Version released, without the tag prefix.</summary>
    public required string Version { get; init; }

    /// <summary>Tag created for the release, prefix included.</summary>
    public required string Tag { get; init; }

    public required DateTimeOffset ReleasedAt { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Commit the release was cut from.</summary>
    public string? CommitSha { get; init; }

    public long? RunId { get; init; }

    /// <summary>Link to the workflow run, so the logs stay one click away.</summary>
    public string? RunUrl { get; init; }

    /// <summary>Why the release failed, when it did.</summary>
    public string? Error { get; init; }

    public string ShortSha => CommitSha is { Length: > 7 } sha ? sha[..7] : CommitSha ?? string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LedgerEntry))]
public partial class LedgerJsonContext : JsonSerializerContext;
