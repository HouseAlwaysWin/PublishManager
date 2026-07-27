namespace PublishManager.Core.Ledger;

/// <summary>
/// The local record of every release performed, kept because tags get pruned
/// and git then remembers nothing about the version that once existed.
/// </summary>
public interface IReleaseLedger
{
    /// <summary>A project's releases, newest first.</summary>
    Task<IReadOnlyList<LedgerEntry>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Records a completed release.</summary>
    Task AppendAsync(LedgerEntry entry, CancellationToken ct = default);
}
