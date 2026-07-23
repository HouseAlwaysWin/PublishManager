namespace PublishManager.Core.Processes;

/// <summary>A single line of process output.</summary>
/// <param name="Text">The line content (without trailing newline).</param>
/// <param name="IsError">True if the line came from stderr.</param>
public readonly record struct ProcessLine(string Text, bool IsError);

/// <summary>Describes an external process to run.</summary>
public sealed record ProcessRequest
{
    /// <summary>Executable name or full path (e.g. "git", "dotnet", "pwsh").</summary>
    public required string FileName { get; init; }

    /// <summary>Arguments passed individually (no shell quoting required).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Working directory; null/empty inherits the current directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Extra environment variables to set (or override) for the child process.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }
}

/// <summary>Result of a completed process run.</summary>
public sealed record ProcessResult(
    int ExitCode,
    IReadOnlyList<string> StdOut,
    IReadOnlyList<string> StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>stdout joined by newlines (trimmed), convenient for single-value commands.</summary>
    public string StdOutText => string.Join('\n', StdOut).Trim();

    /// <summary>stderr joined by newlines (trimmed).</summary>
    public string StdErrText => string.Join('\n', StdErr).Trim();
}

/// <summary>Thrown when a process cannot be started (e.g. executable not found).</summary>
public sealed class ProcessStartException(string fileName, Exception inner)
    : Exception($"Failed to start process '{fileName}'. Is it installed and on PATH?", inner)
{
    public string FileName { get; } = fileName;
}
