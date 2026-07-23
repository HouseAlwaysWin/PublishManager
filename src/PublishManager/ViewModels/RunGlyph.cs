using PublishManager.Core.GitHub;

namespace PublishManager.ViewModels;

/// <summary>Maps a run/job/step state to a compact status glyph for the timeline.</summary>
public static class RunGlyph
{
    public static string For(RunStatus status, RunConclusion conclusion) => status switch
    {
        RunStatus.Completed => conclusion switch
        {
            RunConclusion.Success => "✓",
            RunConclusion.Failure => "✗",
            RunConclusion.Cancelled => "⊘",
            RunConclusion.TimedOut => "⏱",
            RunConclusion.Skipped => "–",
            RunConclusion.ActionRequired => "!",
            _ => "•",
        },
        RunStatus.InProgress => "▶",
        RunStatus.Queued => "○",
        _ => "•",
    };
}
