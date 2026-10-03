using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using Wording.Desktop.ViewModels;

namespace Wording.Desktop.Views;

public partial class PracticeView : UserControl
{
    private bool wasChoosingOptions;
    public PracticeView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PracticeViewModel previous)
                PropertyChangedEventManager.RemoveHandler(previous, OnPracticeStateChanged, string.Empty);
            if (e.NewValue is PracticeViewModel current)
            {
                wasChoosingOptions = current.IsChoosingOptions;
                PropertyChangedEventManager.AddHandler(current, OnPracticeStateChanged, string.Empty);
            }
        };
    }
    private void OnPracticeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PracticeViewModel vm) return;
        if (wasChoosingOptions && !vm.IsChoosingOptions) PageScroll.ScrollToTop();
        wasChoosingOptions = vm.IsChoosingOptions;
    }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ExerciseWorkspace is not null && ActualHeight > 0)
            ExerciseWorkspace.Height = Math.Max(360, ActualHeight - 24);
    }

    private void OnExerciseMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pane = (ScrollViewer)sender;
        if (e.Handled || e.Delta == 0) return;
        if ((e.Delta > 0 && pane.VerticalOffset > 0) || (e.Delta < 0 && pane.VerticalOffset < pane.ScrollableHeight)) return;
        // At an article/question boundary, continue through the page instead of trapping the wheel.
        e.Handled = true;
        PageScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            { RoutedEvent = MouseWheelEvent });
    }
}
