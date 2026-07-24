using Avalonia.Media;
using PublishManager.Core.GitHub;

namespace PublishManager.ViewModels;

/// <summary>
/// Maps a run/job/step state to a status glyph and its colour.
/// Deliberately avoids triangles for "running" — they read as expander arrows.
/// </summary>
public static class RunGlyph
{
    public static readonly IBrush Success = new SolidColorBrush(Color.Parse("#3FB950"));
    public static readonly IBrush Failure = new SolidColorBrush(Color.Parse("#F85149"));
    public static readonly IBrush Running = new SolidColorBrush(Color.Parse("#58A6FF"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#D29922"));
    public static readonly IBrush Pending = new SolidColorBrush(Color.Parse("#8B949E"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E7681"));

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
        RunStatus.InProgress => "●",
        RunStatus.Queued => "○",
        _ => "•",
    };

    public static IBrush BrushFor(RunStatus status, RunConclusion conclusion) => status switch
    {
        RunStatus.Completed => conclusion switch
        {
            RunConclusion.Success => Success,
            RunConclusion.Failure => Failure,
            RunConclusion.TimedOut or RunConclusion.ActionRequired => Warning,
            RunConclusion.Cancelled or RunConclusion.Skipped => Muted,
            _ => Pending,
        },
        RunStatus.InProgress => Running,
        RunStatus.Queued => Pending,
        _ => Muted,
    };
}
