using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WordTrail.Desktop.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public abstract class PageViewModel : ObservableObject
{
    private string error = "";
    private string notice = "";
    private bool isBusy;
    private readonly List<AsyncCommand> commands = [];
    public string Error { get => error; set => SetProperty(ref error, value); }
    public string Notice { get => notice; set => SetProperty(ref notice, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            SetProperty(ref isBusy, value);
            foreach (var command in commands) command.NotifyCanExecuteChanged();
        }
    }

    protected AsyncCommand Command(Func<CancellationToken, Task> action, Func<bool>? canExecute = null)
    {
        var command = new AsyncCommand(async token =>
        {
            Error = "";
            Notice = "";
            IsBusy = true;
            try { await action(token); }
            finally { IsBusy = false; }
        }, message => Error = message, () => !IsBusy && (canExecute?.Invoke() ?? true));
        commands.Add(command);
        return command;
    }

    public virtual Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
