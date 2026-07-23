using System.Text.Json;
using System.Text.Json.Serialization;
using PublishManager.Core.Models;

namespace PublishManager.Core.Storage;

/// <summary>Root document persisted to projects.json (carries a schema version for migrations).</summary>
public sealed class ProjectsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Project> Projects { get; set; } = [];
}

/// <summary>
/// Source-generated (reflection-free, trim-friendly) JSON context for the store.
/// Enums are written as camelCase strings and the file is indented for easy inspection.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectsDocument))]
public partial class StorageJsonContext : JsonSerializerContext;
