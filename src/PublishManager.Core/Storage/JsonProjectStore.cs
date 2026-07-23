using Microsoft.Extensions.Logging;
using PublishManager.Core.Models;

namespace PublishManager.Core.Storage;

/// <summary>
/// JSON-backed <see cref="IProjectStore"/>. Writes are atomic (temp file then
/// <see cref="File.Move(string, string, bool)"/>) so a crash mid-write cannot
/// corrupt the existing file. Access is serialized by a semaphore.
/// </summary>
public sealed class JsonProjectStore(StorageOptions options, ILogger<JsonProjectStore> logger) : IProjectStore
{
    private readonly StorageOptions _options = options;
    private readonly ILogger<JsonProjectStore> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<Project>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = _options.ProjectsPath;
            if (!File.Exists(path))
                return [];

            await using var stream = File.OpenRead(path);
            var document = await System.Text.Json.JsonSerializer
                .DeserializeAsync(stream, StorageJsonContext.Default.ProjectsDocument, ct)
                .ConfigureAwait(false);

            return document?.Projects ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load projects from {Path}", _options.ProjectsPath);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<Project> projects, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_options.BaseDirectory);

            var document = new ProjectsDocument
            {
                SchemaVersion = ProjectsDocument.CurrentSchemaVersion,
                Projects = [.. projects],
            };

            var path = _options.ProjectsPath;
            var tempPath = path + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await System.Text.Json.JsonSerializer
                    .SerializeAsync(stream, document, StorageJsonContext.Default.ProjectsDocument, ct)
                    .ConfigureAwait(false);
            }

            // Atomic on the same volume; overwrites the previous file if present.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save projects to {Path}", _options.ProjectsPath);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
