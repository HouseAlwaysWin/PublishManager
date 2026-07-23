using PublishManager.Core.Versioning;
using Semver;

namespace PublishManager.Tests.Versioning;

public class SemVerServiceTests
{
    private readonly SemVerService _sut = new();

    [Theory]
    [InlineData("v1.2.3", "v", "1.2.3")]
    [InlineData("V1.2.3", "v", "1.2.3")]
    [InlineData("1.2.3", "", "1.2.3")]
    [InlineData("v1.2.3-rc.1", "v", "1.2.3-rc.1")]
    public void TryParseTag_Valid_ReturnsVersion(string tag, string prefix, string expected)
    {
        var version = _sut.TryParseTag(tag, prefix);
        Assert.NotNull(version);
        Assert.Equal(expected, version!.ToString());
    }

    [Theory]
    [InlineData("1.2.3", "v")]      // missing required prefix
    [InlineData("release-1", "v")] // wrong prefix + not semver
    [InlineData("vNotAVersion", "v")]
    [InlineData("", "v")]
    public void TryParseTag_Invalid_ReturnsNull(string tag, string prefix)
    {
        Assert.Null(_sut.TryParseTag(tag, prefix));
    }

    [Fact]
    public void GetLatest_PicksHighest_AndIgnoresInvalid()
    {
        string[] tags = ["v1.0.0", "v1.2.0", "v1.1.5", "not-a-tag", "v0.9.9"];
        var latest = _sut.GetLatest(tags);
        Assert.Equal("1.2.0", latest!.ToString());
    }

    [Fact]
    public void GetLatest_StableOutranksPrerelease()
    {
        string[] tags = ["v1.0.0-rc.1", "v1.0.0-rc.2", "v1.0.0"];
        var latest = _sut.GetLatest(tags);
        Assert.Equal("1.0.0", latest!.ToString());
    }

    [Fact]
    public void GetLatest_NoValidTags_ReturnsNull()
    {
        Assert.Null(_sut.GetLatest(["nope", "alsonope"]));
    }

    [Theory]
    [InlineData("1.2.3", VersionBump.Major, "2.0.0")]
    [InlineData("1.2.3", VersionBump.Minor, "1.3.0")]
    [InlineData("1.2.3", VersionBump.Patch, "1.2.4")]
    public void ComputeNext_MajorMinorPatch(string current, VersionBump bump, string expected)
    {
        var next = _sut.ComputeNext(SemVersion.Parse(current, SemVersionStyles.Strict), bump);
        Assert.Equal(expected, next.ToString());
    }

    [Fact]
    public void ComputeNext_Prerelease_FromStable_StartsSeriesOnNextPatch()
    {
        var next = _sut.ComputeNext(SemVersion.Parse("1.2.3", SemVersionStyles.Strict), VersionBump.Prerelease);
        Assert.Equal("1.2.4-rc.1", next.ToString());
    }

    [Fact]
    public void ComputeNext_Prerelease_IncrementsExistingCounter()
    {
        var next = _sut.ComputeNext(SemVersion.Parse("1.2.4-rc.1", SemVersionStyles.Strict), VersionBump.Prerelease);
        Assert.Equal("1.2.4-rc.2", next.ToString());
    }

    [Fact]
    public void ComputeNext_Prerelease_NonNumericTail_AppendsOne()
    {
        var next = _sut.ComputeNext(SemVersion.Parse("1.2.4-beta", SemVersionStyles.Strict), VersionBump.Prerelease);
        Assert.Equal("1.2.4-beta.1", next.ToString());
    }

    [Fact]
    public void ComputeNext_Prerelease_CustomLabel()
    {
        var next = _sut.ComputeNext(SemVersion.Parse("1.0.0", SemVersionStyles.Strict), VersionBump.Prerelease, "alpha");
        Assert.Equal("1.0.1-alpha.1", next.ToString());
    }

    [Fact]
    public void ComputeNextFromTags_UsesLatestAndPrefixesResult()
    {
        string[] tags = ["v1.0.0", "v1.4.2", "v1.4.1"];
        var result = _sut.ComputeNextFromTags(tags, VersionBump.Minor);
        Assert.Equal("v1.4.2", result.CurrentTag);
        Assert.Equal("v1.5.0", result.NextTag);
        Assert.True(result.HadPrevious);
    }

    [Theory]
    [InlineData(VersionBump.Major, "v1.0.0")]
    [InlineData(VersionBump.Minor, "v0.1.0")]
    [InlineData(VersionBump.Patch, "v0.0.1")]
    public void ComputeNextFromTags_NoTags_BasesOnZero(VersionBump bump, string expected)
    {
        var result = _sut.ComputeNextFromTags([], bump);
        Assert.Null(result.CurrentTag);
        Assert.False(result.HadPrevious);
        Assert.Equal(expected, result.NextTag);
    }

    [Theory]
    [InlineData("1.2.3", "v", "1.2.3")]     // prefix optional
    [InlineData("v1.2.3", "v", "1.2.3")]    // prefix present
    [InlineData("2.0.0-rc.1", "v", "2.0.0-rc.1")]
    public void ParseVersion_Lenient_Valid(string input, string prefix, string expected)
    {
        var version = _sut.ParseVersion(input, prefix);
        Assert.NotNull(version);
        Assert.Equal(expected, version!.ToString());
    }

    [Theory]
    [InlineData("not-a-version", "v")]
    [InlineData("1.2", "v")]
    [InlineData("", "v")]
    public void ParseVersion_Invalid_ReturnsNull(string input, string prefix)
    {
        Assert.Null(_sut.ParseVersion(input, prefix));
    }
}
