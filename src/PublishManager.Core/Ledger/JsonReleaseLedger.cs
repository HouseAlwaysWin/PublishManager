using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Storage;

namespace PublishManager.Core.Ledger;

/// <summary>
/// Newline-delimited JSON ledger: one release per line. Appending never
/// rewrites what is already there, so a crash mid-write can cost at most the
/// release being recorded — and an unreadable line is skipped rather than
/// taking the rest of the history with it.
/// </summary>
public sealed class JsonReleaseLedger(StorageOptions options, ILogger<JsonReleaseLedger> logger) : IReleaseLedger
{
    private readonly StorageOptions _options = options;
    private readonly ILogger<JsonReleaseLedger> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<LedgerEntry>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = _options.LedgerPath;
            if (!File.Exists(path))
                return [];

            var entries = new List<LedgerEntry>();
            foreach (var line in await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerEntry);
                    if (entry is not null && entry.ProjectId == projectId)
                        entries.Add(entry);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping an unreadable line in the release ledger.");
                }
            }

            return [.. entries.OrderByDescending(e => e.ReleasedAt)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_options.BaseDirectory);

            var line = JsonSerializer.Serialize(entry, LedgerJsonContext.Default.LedgerEntry);
            await File.AppendAllTextAsync(
                _options.LedgerPath, line + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
