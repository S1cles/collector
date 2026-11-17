using System.Windows.Input;

namespace SpectraCollector.App;

/// <summary>A synchronous command; its owner announces availability changes with <see cref="RaiseCanExecuteChanged"/>.</summary>
public sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    /// <summary>A command that is always available.</summary>
    public RelayCommand(Action execute)
        : this(execute, static () => true)
    {
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter) => execute();

    /// <summary>Makes bound controls query <see cref="CanExecute"/> again.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
