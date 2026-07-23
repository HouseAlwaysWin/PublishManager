namespace PublishManager.Core.Storage;

/// <summary>Where PublishManager persists its data. Injected so tests can point at a temp dir.</summary>
public sealed class StorageOptions
{
    /// <summary>Base directory for all data files (created on first write).</summary>
    public required string BaseDirectory { get; init; }

    public string ProjectsFileName { get; init; } = "projects.json";

    public string ProjectsPath => Path.Combine(BaseDirectory, ProjectsFileName);

    /// <summary>The conventional per-user location: %APPDATA%\PublishManager.</summary>
    public static StorageOptions Default => new()
    {
        BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PublishManager"),
    };
}
