using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Ledger;
using PublishManager.Core.Storage;

namespace PublishManager.Tests.Ledger;

public sealed class ReleaseLedgerTests : IDisposable
{
    private readonly string _dir;
    private readonly StorageOptions _options;
    private readonly JsonReleaseLedger _sut;

    public ReleaseLedgerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pm-ledger-" + Guid.NewGuid().ToString("N"));
        _options = new StorageOptions { BaseDirectory = _dir };
        _sut = new JsonReleaseLedger(_options, NullLogger<JsonReleaseLedger>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static LedgerEntry Entry(Guid projectId, string tag, DateTimeOffset at) => new()
    {
        ProjectId = projectId,
        Version = tag.TrimStart('v'),
        Tag = tag,
        ReleasedAt = at,
        Succeeded = true,
        CommitSha = "abc1234def",
        RunId = 42,
        RunUrl = "https://github.com/o/r/actions/runs/42",
    };

    [Fact]
    public async Task NoFileYet_ListsNothing()
    {
        Assert.Empty(await _sut.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AppendedRelease_IsRememberedInFull()
    {
        var project = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-03-14T09:30:00Z");

        await _sut.AppendAsync(Entry(project, "v0.20.0", at));

        var only = Assert.Single(await _sut.ListAsync(project));
        Assert.Equal("v0.20.0", only.Tag);
        Assert.Equal("0.20.0", only.Version);
        Assert.Equal(at, only.ReleasedAt);
        Assert.Equal("abc1234def", only.CommitSha);
        Assert.Equal(42, only.RunId);
        Assert.True(only.Succeeded);
    }

    [Fact]
    public async Task ReleasesAreListedNewestFirst()
    {
        var project = Guid.NewGuid();
        await _sut.AppendAsync(Entry(project, "v1.0.0", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await _sut.AppendAsync(Entry(project, "v1.2.0", DateTimeOffset.Parse("2026-03-01T00:00:00Z")));
        await _sut.AppendAsync(Entry(project, "v1.1.0", DateTimeOffset.Parse("2026-02-01T00:00:00Z")));

        var entries = await _sut.ListAsync(project);

        Assert.Equal(["v1.2.0", "v1.1.0", "v1.0.0"], entries.Select(e => e.Tag));
    }

    [Fact]
    public async Task EachProjectSeesOnlyItsOwnReleases()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await _sut.AppendAsync(Entry(mine, "v1.0.0", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await _sut.AppendAsync(Entry(theirs, "v9.0.0", DateTimeOffset.Parse("2026-01-02T00:00:00Z")));

        var entries = await _sut.ListAsync(mine);

        Assert.Equal("v1.0.0", Assert.Single(entries).Tag);
    }

    [Fact]
    public async Task ReleaseSurvivesItsTagBeingPruned()
    {
        // The whole point of the ledger: git and GitHub forget, this does not.
        var project = Guid.NewGuid();
        await _sut.AppendAsync(Entry(project, "v0.20.0", DateTimeOffset.Parse("2026-03-14T09:30:00Z")));

        var reopened = new JsonReleaseLedger(_options, NullLogger<JsonReleaseLedger>.Instance);

        Assert.Equal("v0.20.0", Assert.Single(await reopened.ListAsync(project)).Tag);
    }

    [Fact]
    public async Task ACorruptLineDoesNotTakeTheOthersWithIt()
    {
        var project = Guid.NewGuid();
        await _sut.AppendAsync(Entry(project, "v1.0.0", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await File.AppendAllTextAsync(_options.LedgerPath, "{ this is not json" + Environment.NewLine);
        await _sut.AppendAsync(Entry(project, "v1.1.0", DateTimeOffset.Parse("2026-02-01T00:00:00Z")));

        var entries = await _sut.ListAsync(project);

        Assert.Equal(["v1.1.0", "v1.0.0"], entries.Select(e => e.Tag));
    }
}
