using Microsoft.Extensions.Logging.Abstractions;
using PublishManager.Core.Models;
using PublishManager.Core.Storage;
using PublishManager.Core.Versioning;

namespace PublishManager.Tests.Storage;

public sealed class JsonProjectStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly StorageOptions _options;
    private readonly JsonProjectStore _sut;

    public JsonProjectStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pm-tests-" + Guid.NewGuid().ToString("N"));
        _options = new StorageOptions { BaseDirectory = _dir };
        _sut = new JsonProjectStore(_options, NullLogger<JsonProjectStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public async Task Load_WhenNoFile_ReturnsEmpty()
    {
        var projects = await _sut.LoadAsync();
        Assert.Empty(projects);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var project = new Project
        {
            Name = "My App",
            LocalPath = @"D:\DotNetProjects\MyApp",
            Kind = ProjectKind.DotNet,
            ReleaseModel = ReleaseModel.WorkflowDispatch,
            TagPrefix = "v",
            DefaultBump = VersionBump.Minor,
            ReleaseBranch = "main",
            Owner = "HouseAlwaysWin",
            Repo = "MyApp",
            WorkflowFile = "release.yml",
            StampVersionIntoBuild = true,
            Steps =
            {
                new ReleaseStep { Name = "build", Interpreter = StepInterpreter.PowerShell, Command = "dotnet build" },
                new ReleaseStep { Name = "tool", Interpreter = StepInterpreter.Executable, Command = "dotnet", Arguments = { "pack" }, ContinueOnError = true },
            },
            DispatchInputs = { ["environment"] = "production" },
        };

        await _sut.SaveAsync([project]);
        var loaded = await _sut.LoadAsync();

        var only = Assert.Single(loaded);
        Assert.Equal(project.Id, only.Id);
        Assert.Equal("My App", only.Name);
        Assert.Equal(ReleaseModel.WorkflowDispatch, only.ReleaseModel);
        Assert.Equal(VersionBump.Minor, only.DefaultBump);
        Assert.Equal("release.yml", only.WorkflowFile);
        Assert.True(only.StampVersionIntoBuild);
        Assert.Equal(2, only.Steps.Count);
        Assert.Equal(StepInterpreter.Executable, only.Steps[1].Interpreter);
        Assert.Equal("pack", only.Steps[1].Arguments[0]);
        Assert.Equal("production", only.DispatchInputs["environment"]);
    }

    [Fact]
    public async Task Save_Twice_OverwritesAndLeavesNoTempFile()
    {
        await _sut.SaveAsync([new Project { Name = "First" }]);
        await _sut.SaveAsync([new Project { Name = "Second" }, new Project { Name = "Third" }]);

        var loaded = await _sut.LoadAsync();
        Assert.Equal(2, loaded.Count);
        Assert.False(File.Exists(_options.ProjectsPath + ".tmp"), "temp file should not linger after save");
    }

    [Fact]
    public async Task SavedFile_UsesStringEnumsAndSchemaVersion()
    {
        await _sut.SaveAsync([new Project { Name = "X", ReleaseModel = ReleaseModel.TagPush }]);

        var json = await File.ReadAllTextAsync(_options.ProjectsPath);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"releaseModel\": \"TagPush\"", json); // enum serialized as its name string
    }
}
