using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WordTrail.Desktop.ViewModels;

namespace WordTrail.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox) return;
        if (Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat) return;
        if (DataContext is not MainViewModel { CurrentPage: ReviewViewModel review }) return;
        ICommand? command = e.Key switch
        {
            Key.Space => review.FlipCommand,
            Key.D1 or Key.NumPad1 => review.AgainCommand,
            Key.D2 or Key.NumPad2 => review.HardCommand,
            Key.D3 or Key.NumPad3 => review.GoodCommand,
            Key.D4 or Key.NumPad4 => review.EasyCommand,
            _ => null
        };
        if (command?.CanExecute(null) != true) return;
        command.Execute(null);
        e.Handled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel { CurrentPage.IsBusy: true } model)
        {
            model.CurrentPage.Notice = "正在保存或產生內容，請完成或取消後再關閉視窗。";
            e.Cancel = true;
        }
    }
}
