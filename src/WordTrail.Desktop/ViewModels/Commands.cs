using System.Windows.Input;

namespace WordTrail.Desktop.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>所有非同步按鈕共用此處，避免重按、未觀察例外與取消後仍顯示成功。</summary>
public sealed class AsyncCommand : ObservableObject, ICommand
{
    private readonly Func<CancellationToken, Task> execute;
    private readonly Action<string> reportError;
    private readonly Func<bool>? canExecute;
    private CancellationTokenSource? cancellation;
    private bool isRunning;

    public AsyncCommand(Func<CancellationToken, Task> execute, Action<string> reportError, Func<bool>? canExecute = null)
    { this.execute = execute; this.reportError = reportError; this.canExecute = canExecute; }

    public bool IsRunning { get => isRunning; private set => SetProperty(ref isRunning, value); }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        cancellation = new CancellationTokenSource();
        IsRunning = true;
        NotifyCanExecuteChanged();
        try { await execute(cancellation.Token); }
        catch (OperationCanceledException) { reportError("已取消，未套用尚未完成的內容。"); }
        catch (Exception exception) { reportError(exception.Message); }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            IsRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => cancellation?.Cancel();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
