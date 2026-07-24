using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PublishManager.Core.GitHub;

namespace PublishManager.ViewModels;

/// <summary>One step within a job, with live status.</summary>
public partial class StepLineViewModel : ViewModelBase
{
    public StepLineViewModel(StepSnapshot step)
    {
        Number = step.Number;
        Name = step.Name;
        _status = step.Status;
        _conclusion = step.Conclusion;
    }

    public int Number { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private RunStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private RunConclusion _conclusion;

    public string Glyph => RunGlyph.For(Status, Conclusion);
    public IBrush GlyphBrush => RunGlyph.BrushFor(Status, Conclusion);

    public void Update(StepSnapshot step)
    {
        Status = step.Status;
        Conclusion = step.Conclusion;
    }
}

/// <summary>One job within a run, with its steps, updated in place across polls.</summary>
public partial class JobLineViewModel : ViewModelBase
{
    public JobLineViewModel(JobSnapshot job)
    {
        Id = job.Id;
        Name = job.Name;
        _status = job.Status;
        _conclusion = job.Conclusion;
        Update(job);
    }

    public long Id { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private RunStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(GlyphBrush))]
    private RunConclusion _conclusion;

    public ObservableCollection<StepLineViewModel> Steps { get; } = [];

    public string Glyph => RunGlyph.For(Status, Conclusion);
    public IBrush GlyphBrush => RunGlyph.BrushFor(Status, Conclusion);

    public void Update(JobSnapshot job)
    {
        Status = job.Status;
        Conclusion = job.Conclusion;

        for (var i = 0; i < job.Steps.Count; i++)
        {
            if (i < Steps.Count)
                Steps[i].Update(job.Steps[i]);
            else
                Steps.Add(new StepLineViewModel(job.Steps[i]));
        }
        while (Steps.Count > job.Steps.Count)
            Steps.RemoveAt(Steps.Count - 1);
    }
}
