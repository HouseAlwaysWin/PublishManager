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
            Trigger = ReleaseTrigger.WorkflowDispatch,
            TagPrefix = "v",
            DefaultBump = VersionBump.Minor,
            ReleaseBranch = "main",
            Owner = "HouseAlwaysWin",
            Repo = "MyApp",
            WorkflowFile = "release.yml",
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
        Assert.Equal(ReleaseTrigger.WorkflowDispatch, only.Trigger);
        Assert.Equal(VersionBump.Minor, only.DefaultBump);
        Assert.Equal("release.yml", only.WorkflowFile);
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
    public async Task Load_FileWrittenBeforeTheRename_KeepsItsTrigger()
    {
        // Verbatim shape of a projects.json written before ReleaseModel was
        // renamed to Trigger and Kind/StampVersionIntoBuild were removed. The
        // trigger must survive; a silent fall back to TagPush would quietly
        // change how the project releases.
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(_options.ProjectsPath, """
        {
          "schemaVersion": 1,
          "projects": [
            {
              "id": "4ea2742b-e6bb-423d-bddc-c015300eeb80",
              "name": "GimmeCapture",
              "localPath": "D:\\DotNetProjects\\GimmeCapture",
              "kind": "DotNet",
              "releaseModel": "WorkflowDispatch",
              "tagPrefix": "v",
              "defaultBump": "Patch",
              "steps": [],
              "dispatchInputs": {},
              "stampVersionIntoBuild": true
            }
          ]
        }
        """);

        var loaded = await _sut.LoadAsync();

        var only = Assert.Single(loaded);
        Assert.Equal(ReleaseTrigger.WorkflowDispatch, only.Trigger);
        Assert.Equal("GimmeCapture", only.Name);
        Assert.Equal(Guid.Parse("4ea2742b-e6bb-423d-bddc-c015300eeb80"), only.Id);
    }

    [Fact]
    public async Task SavedFile_UsesStringEnumsAndSchemaVersion()
    {
        await _sut.SaveAsync([new Project { Name = "X", Trigger = ReleaseTrigger.TagPush }]);

        var json = await File.ReadAllTextAsync(_options.ProjectsPath);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"releaseModel\": \"TagPush\"", json); // enum serialized as its name string
    }
}
