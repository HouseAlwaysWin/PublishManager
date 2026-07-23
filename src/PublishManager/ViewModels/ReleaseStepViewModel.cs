using CommunityToolkit.Mvvm.ComponentModel;
using PublishManager.Core.Models;

namespace PublishManager.ViewModels;

/// <summary>Editable view model for a single <see cref="ReleaseStep"/>.</summary>
public partial class ReleaseStepViewModel : ViewModelBase
{
    public ReleaseStepViewModel() : this(new ReleaseStep()) { }

    public ReleaseStepViewModel(ReleaseStep step)
    {
        _name = step.Name;
        _interpreter = step.Interpreter;
        _command = step.Command;
        _argumentsText = string.Join(' ', step.Arguments);
        _workingDirectory = step.WorkingDirectory ?? string.Empty;
        _continueOnError = step.ContinueOnError;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private StepInterpreter _interpreter;
    [ObservableProperty] private string _command;
    [ObservableProperty] private string _argumentsText;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private bool _continueOnError;

    public StepInterpreter[] Interpreters { get; } = Enum.GetValues<StepInterpreter>();

    public ReleaseStep ToModel() => new()
    {
        Name = Name?.Trim() ?? string.Empty,
        Interpreter = Interpreter,
        Command = Command ?? string.Empty,
        Arguments = string.IsNullOrWhiteSpace(ArgumentsText)
            ? []
            : [.. ArgumentsText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
        ContinueOnError = ContinueOnError,
    };
}
