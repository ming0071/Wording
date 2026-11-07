using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Wording.Desktop.ViewModels;
namespace Wording.Desktop.Views;
public partial class ReviewView : UserControl
{
    public static readonly DependencyProperty IsCompactLayoutProperty = DependencyProperty.Register(
        nameof(IsCompactLayout), typeof(bool), typeof(ReviewView), new PropertyMetadata(false, OnLayoutModeChanged));
    public bool IsCompactLayout { get => (bool)GetValue(IsCompactLayoutProperty); set => SetValue(IsCompactLayoutProperty, value); }
    private ReviewViewModel? observed;
    public ReviewView()
    {
        InitializeComponent();
        SetBinding(IsCompactLayoutProperty, new Binding(nameof(MainWindow.IsCompactReview))
        { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(MainWindow), 1) });
        Focusable = true;
        IsTabStop = false;
        Loaded += (_, _) => { ReviewInputLanguage.Configure(this); Observe(); RequestKeyboardFocus(); };
        Unloaded += (_, _) => { ScopePopup.IsOpen = false; StopObserving(); };
        DataContextChanged += (_, _) => { ScopePopup.IsOpen = false; Observe(); UpdateDefinitionWidth(); DefinitionScroll.ScrollToTop(); if (IsLoaded) RequestKeyboardFocus(); };
        DefinitionScroll.SizeChanged += (_, _) => UpdateDefinitionWidth();
        DefinitionScroll.ScrollChanged += (_, _) => UpdateDefinitionWidth();
    }
    private static void OnLayoutModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ReviewView)sender;
        if (e.NewValue is false && view.ScopePopup is not null) view.ScopePopup.IsOpen = false;
    }
    private void OnCloseScopePopup(object sender, RoutedEventArgs e) => ScopePopup.IsOpen = false;
    private void OnScopePopupClosed(object? sender, EventArgs e) => RequestKeyboardFocus();
    private void OnScopePopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ScopePopup.IsOpen = false;
        e.Handled = true;
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
        if (e.PropertyName is nameof(ReviewViewModel.Current) or nameof(ReviewViewModel.IsAnswerVisible))
        {
            DefinitionScroll.ScrollToTop();
            UpdateDefinitionWidth();
            RequestKeyboardFocus();
        }
    }
    private void UpdateDefinitionWidth()
    {
        if (DataContext is ReviewViewModel review)
            review.UpdateDefinitionWidth(DefinitionScroll.ViewportWidth > 0 ? DefinitionScroll.ViewportWidth : DefinitionScroll.ActualWidth);
    }
    private void OnHeadwordDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        HeadwordText.Focus();
        HeadwordText.SelectAll();
        e.Handled = true;
    }
    private void RequestKeyboardFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        // The dashboard's clicked button has been removed. Restore a live keyboard route to the window.
        if (IsLoaded && IsVisible && !ScopePopup.IsOpen && Window.GetWindow(this)?.IsActive == true) Keyboard.Focus(this);
    }));
}
