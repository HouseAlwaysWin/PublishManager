namespace PublishManager.Core.Storage;

/// <summary>Where PublishManager persists its data. Injected so tests can point at a temp dir.</summary>
public sealed class StorageOptions
{
    /// <summary>Base directory for all data files (created on first write).</summary>
    public required string BaseDirectory { get; init; }

    public string ProjectsFileName { get; init; } = "projects.json";

    /// <summary>Newline-delimited JSON, one release per line.</summary>
    public string LedgerFileName { get; init; } = "releases.ndjson";

    public string ProjectsPath => Path.Combine(BaseDirectory, ProjectsFileName);

    public string LedgerPath => Path.Combine(BaseDirectory, LedgerFileName);

    /// <summary>Where diagnostic logs are written, so failures leave a trail.</summary>
    public string LogDirectory => Path.Combine(BaseDirectory, "logs");

    /// <summary>The conventional per-user location: %APPDATA%\PublishManager.</summary>
    public static StorageOptions Default => new()
    {
        BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PublishManager"),
    };
}
