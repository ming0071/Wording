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
        if (DataContext is not MainViewModel { CurrentPage: ReviewViewModel review }) return;
        e.Handled = ReviewKeyboard.Handle(review, e.Key, Keyboard.Modifiers, e.IsRepeat,
            Keyboard.FocusedElement as DependencyObject);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel { CurrentPage.IsBusy: true } model)
        {
            model.CurrentPage.Notice = "正在保存或產生內容，請完成或取消後再關閉視窗。";
            e.Cancel = true;
            return;
        }
        if (DataContext is MainViewModel shell && !shell.ConfirmLeaveEditor()) e.Cancel = true;
    }
}
