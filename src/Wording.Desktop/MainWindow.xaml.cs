using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wording.Desktop.ViewModels;

namespace Wording.Desktop;

public partial class MainWindow : Window
{
    private static readonly DependencyPropertyKey IsCompactReviewPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompactReview), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactReviewProperty = IsCompactReviewPropertyKey.DependencyProperty;
    public bool IsCompactReview => (bool)GetValue(IsCompactReviewProperty);
    private MainViewModel? observed;

    public MainWindow()
    {
        InitializeComponent();
        LayoutRoot.SizeChanged += (_, _) => UpdateReviewLayout();
        StateChanged += (_, _) => UpdateReviewLayout();
        DataContextChanged += (_, _) =>
        {
            if (observed is not null) observed.PropertyChanged -= OnModelChanged;
            observed = DataContext as MainViewModel;
            if (observed is not null) observed.PropertyChanged += OnModelChanged;
            UpdateReviewLayout();
        };
        Closed += (_, _) => { if (observed is not null) observed.PropertyChanged -= OnModelChanged; };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage)) UpdateReviewLayout();
    }

    private void UpdateReviewLayout() => SetValue(IsCompactReviewPropertyKey,
        DataContext is MainViewModel { CurrentPage: ReviewViewModel } && WindowState != WindowState.Maximized &&
        LayoutRoot.ActualHeight is > 0 and < 900);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (WindowKeyboard.Handle(this, e.Key, Keyboard.Modifiers, e.IsRepeat, focused))
        {
            e.Handled = true;
            return;
        }
        if (DataContext is not MainViewModel { CurrentPage: ReviewViewModel review }) return;
        e.Handled = ReviewKeyboard.Handle(review, e.Key, Keyboard.Modifiers, e.IsRepeat,
            focused);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel updating && updating.Updates.IsDownloading)
        {
            updating.Updates.CancelCommand.Execute(null);
            updating.Updates.Notice = "正在取消下載，完成後即可關閉程式。";
            e.Cancel = true;
            return;
        }
        if (DataContext is MainViewModel savingUpdate && savingUpdate.Updates.Phase == Wording.Core.UpdatePhase.Installing && !savingUpdate.Updates.HasLaunchedInstaller)
        {
            savingUpdate.Updates.Notice = "正在備份學習資料，請稍候。";
            e.Cancel = true;
            return;
        }
        if (DataContext is MainViewModel { CurrentPage.IsBusy: true } model)
        {
            model.CurrentPage.Notice = "正在保存或產生內容，請完成或取消後再關閉視窗。";
            e.Cancel = true;
            return;
        }
        if (DataContext is MainViewModel shell && !shell.ConfirmLeaveEditor()) e.Cancel = true;
    }
}
