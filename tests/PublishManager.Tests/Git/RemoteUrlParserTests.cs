using PublishManager.Core.Git;

namespace PublishManager.Tests.Git;

public class RemoteUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/HouseAlwaysWin/PublishManager.git", "HouseAlwaysWin", "PublishManager")]
    [InlineData("https://github.com/HouseAlwaysWin/PublishManager", "HouseAlwaysWin", "PublishManager")]
    [InlineData("git@github.com:HouseAlwaysWin/PublishManager.git", "HouseAlwaysWin", "PublishManager")]
    [InlineData("git@github.com:HouseAlwaysWin/PublishManager", "HouseAlwaysWin", "PublishManager")]
    [InlineData("ssh://git@github.com/HouseAlwaysWin/PublishManager.git", "HouseAlwaysWin", "PublishManager")]
    [InlineData("https://github.com/octocat/Hello-World.git ", "octocat", "Hello-World")]
    [InlineData("HTTPS://GitHub.com/Owner/Repo.GIT", "Owner", "Repo")]
    public void TryParse_ValidUrls_ReturnsOwnerRepo(string url, string owner, string repo)
    {
        var ok = RemoteUrlParser.TryParse(url, out var slug);

        Assert.True(ok);
        Assert.Equal(owner, slug.Owner);
        Assert.Equal(repo, slug.Repo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("https://github.com/only-owner")]
    public void TryParse_InvalidUrls_ReturnsFalse(string? url)
    {
        var ok = RemoteUrlParser.TryParse(url, out var slug);

        Assert.False(ok);
        Assert.Equal(default, slug);
    }
}
