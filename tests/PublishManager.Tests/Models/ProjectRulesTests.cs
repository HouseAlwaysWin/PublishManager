using PublishManager.Core.Models;

namespace PublishManager.Tests.Models;

public class ProjectRulesTests
{
    private static Project At(string path, string prefix = "v") => new()
    {
        Name = "P",
        LocalPath = path,
        TagPrefix = prefix,
    };

    [Fact]
    public void TwoProjectsOnOneRepoWithTheSamePrefix_Conflict()
    {
        // Both would manage the same tags: deleting in one leaves the other stale.
        var existing = At(@"D:\repos\App");
        var candidate = At(@"D:\repos\App");

        var conflict = ProjectRules.FindConflict([existing], candidate);

        Assert.NotNull(conflict);
    }

    [Fact]
    public void TwoProjectsOnOneRepoWithDifferentPrefixes_AreSeparateReleaseLines()
    {
        var existing = At(@"D:\repos\Mono", "app-v");
        var candidate = At(@"D:\repos\Mono", "lib-v");

        Assert.Null(ProjectRules.FindConflict([existing], candidate));
    }

    [Fact]
    public void SamePrefixInDifferentRepos_IsFine()
    {
        var existing = At(@"D:\repos\One");
        var candidate = At(@"D:\repos\Two");

        Assert.Null(ProjectRules.FindConflict([existing], candidate));
    }

    [Fact]
    public void EditingAProject_DoesNotConflictWithItself()
    {
        var existing = At(@"D:\repos\App");
        var edited = At(@"D:\repos\App");
        edited.Id = existing.Id;

        Assert.Null(ProjectRules.FindConflict([existing], edited));
    }

    [Theory]
    [InlineData(@"D:\repos\App", @"d:\REPOS\app")]        // Windows paths are case-insensitive
    [InlineData(@"D:\repos\App", @"D:\repos\App\")]       // trailing separator
    [InlineData(@"D:\repos\App", @"  D:\repos\App  ")]    // surrounding whitespace
    public void PathsThatNameTheSameFolder_Conflict(string existingPath, string candidatePath)
    {
        var conflict = ProjectRules.FindConflict([At(existingPath)], At(candidatePath));

        Assert.NotNull(conflict);
    }

    [Fact]
    public void PrefixComparisonIsCaseSensitive_SoVeeAndCapitalVeeAreDistinctLines()
    {
        var existing = At(@"D:\repos\App", "v");
        var candidate = At(@"D:\repos\App", "V");

        Assert.Null(ProjectRules.FindConflict([existing], candidate));
    }

    [Fact]
    public void ConflictNamesTheProjectItClashesWith()
    {
        var existing = At(@"D:\repos\App");
        existing.Name = "既有專案";

        var conflict = ProjectRules.FindConflict([existing], At(@"D:\repos\App"));

        Assert.Contains("既有專案", conflict);
    }
}
