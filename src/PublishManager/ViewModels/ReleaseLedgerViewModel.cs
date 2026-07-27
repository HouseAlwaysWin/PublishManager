using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PublishManager.Core.Ledger;
using PublishManager.Core.Models;

namespace PublishManager.ViewModels;

/// <summary>One remembered release in the ledger list.</summary>
public sealed class LedgerEntryRowViewModel(LedgerEntry entry) : ViewModelBase
{
    public LedgerEntry Entry { get; } = entry;

    public string Tag => Entry.Tag;
    public string ShortSha => Entry.ShortSha;
    public string ReleasedAtText => Entry.ReleasedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Glyph => Entry.Succeeded ? "✓" : "✗";
    public IBrush GlyphBrush => Entry.Succeeded ? RunGlyph.Success : RunGlyph.Failure;
    public string? Error => Entry.Error;
    public bool HasRunUrl => !string.IsNullOrWhiteSpace(Entry.RunUrl);
}

/// <summary>
/// Reads the Release Ledger for one project. Read-only by nature: these are
/// events that already happened, and unlike tags there is nothing to act on.
/// </summary>
public partial class ReleaseLedgerViewModel : ViewModelBase
{
    private readonly IReleaseLedger _ledger;
    private readonly ILogger<ReleaseLedgerViewModel> _logger;
    private Project? _project;

    public ReleaseLedgerViewModel(IReleaseLedger ledger, ILogger<ReleaseLedgerViewModel> logger)
    {
        _ledger = ledger;
        _logger = logger;
    }

    public ObservableCollection<LedgerEntryRowViewModel> Releases { get; } = [];

    [ObservableProperty] private string _title = "發版歷史";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;

    public void Initialize(Project project)
    {
        _project = project;
        Title = $"發版歷史 — {project.Name}";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_project is null)
            return;

        IsBusy = true;
        Status = null;
        try
        {
            var entries = await _ledger.ListAsync(_project.Id);
            Releases.Clear();
            foreach (var entry in entries)
                Releases.Add(new LedgerEntryRowViewModel(entry));

            if (Releases.Count == 0)
                Status = "這個專案還沒有用 PublishManager 發過版。此處只會列出本 app 發出的版本。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the release ledger.");
            Status = $"讀取發版歷史失敗:{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
