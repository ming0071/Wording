using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wording.Desktop.ViewModels;
namespace Wording.Desktop.Views;
public partial class ReviewView : UserControl
{
    private ReviewViewModel? observed;
    public ReviewView()
    {
        InitializeComponent();
        Focusable = true;
        IsTabStop = false;
        Loaded += (_, _) => { Observe(); RequestKeyboardFocus(); };
        Unloaded += (_, _) => StopObserving();
        DataContextChanged += (_, _) => { if (IsLoaded) { Observe(); RequestKeyboardFocus(); } };
    }
    private void Observe()
    {
        StopObserving();
        observed = DataContext as ReviewViewModel;
        if (observed is not null) observed.PropertyChanged += OnReviewChanged;
    }
    private void StopObserving()
    {
        if (observed is not null) observed.PropertyChanged -= OnReviewChanged;
        observed = null;
    }
    private void OnReviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewViewModel.Current) or nameof(ReviewViewModel.IsAnswerVisible)) RequestKeyboardFocus();
    }
    private void RequestKeyboardFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        // The dashboard's clicked button has been removed. Restore a live keyboard route to the window.
        if (IsLoaded && IsVisible && Window.GetWindow(this)?.IsActive == true) Keyboard.Focus(this);
    }));
}
