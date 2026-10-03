using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WordTrail.Desktop.ViewModels;

namespace WordTrail.Desktop.Views;

public partial class ActivityHeatmap : UserControl
{
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(nameof(Activity), typeof(LearningActivity),
        typeof(ActivityHeatmap), new PropertyMetadata(null, (d, _) => ((ActivityHeatmap)d).Rebuild()));
    public LearningActivity? Activity { get => (LearningActivity?)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }
    public static readonly DependencyProperty SelectDayCommandProperty = DependencyProperty.Register(nameof(SelectDayCommand), typeof(ICommand),
        typeof(ActivityHeatmap), new PropertyMetadata(null, (d, _) => ((ActivityHeatmap)d).Rebuild()));
    public ICommand? SelectDayCommand { get => (ICommand?)GetValue(SelectDayCommandProperty); set => SetValue(SelectDayCommandProperty, value); }

    public ActivityHeatmap() => InitializeComponent();

    private void Rebuild()
    {
        if (Chart is null) return;
        Chart.Children.Clear(); Chart.RowDefinitions.Clear(); Chart.ColumnDefinitions.Clear();
        if (Activity is not { Weeks.Count: > 0 } activity) return;
        Chart.ColumnDefinitions.Add(new() { Width = new GridLength(38) });
        foreach (var week in activity.Weeks) Chart.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Chart.RowDefinitions.Add(new() { Height = new GridLength(25) });
        for (var row = 0; row < 7; row++) Chart.RowDefinitions.Add(new() { Height = new GridLength(15) });
        string[] weekdays = ["Sun", "", "Tue", "", "Thu", "", "Sat"];
        for (var row = 0; row < 7; row++)
        {
            var label = new TextBlock { Text = weekdays[row], FontSize = 11, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row + 1); Chart.Children.Add(label);
        }
        for (var column = 0; column < activity.Weeks.Count; column++)
        {
            var week = activity.Weeks[column];
            if (week.MonthLabel.Length > 0)
            {
                var span = Math.Min(3, activity.Weeks.Count);
                var firstColumn = Math.Min(column, activity.Weeks.Count - span);
                var label = new TextBlock { Text = week.MonthLabel, FontSize = 12, Foreground = Brushes.SlateGray,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0),
                    HorizontalAlignment = firstColumn == column ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                Grid.SetColumn(label, firstColumn + 1); Grid.SetColumnSpan(label, span);
                AutomationProperties.SetAutomationId(label, "HeatmapMonth"); Chart.Children.Add(label);
            }
            for (var row = 0; row < week.Days.Count; row++)
            {
                var day = week.Days[row];
                if (!day.InRange) continue;
                var cell = new Button { Style = (Style)Resources["ActivityCell"], Background = (Brush)new BrushConverter().ConvertFromString(day.Color)!,
                    ToolTip = day.Description, Command = SelectDayCommand, CommandParameter = day };
                Grid.SetColumn(cell, column + 1); Grid.SetRow(cell, row + 1);
                AutomationProperties.SetName(cell, day.Description);
                AutomationProperties.SetAutomationId(cell, "ActivityDay"); Chart.Children.Add(cell);
            }
        }
        ResizeRows();
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) { if (e.WidthChanged) ResizeRows(); }
    private void ResizeRows()
    {
        if (Activity is not { Weeks.Count: > 0 } activity || Chart.RowDefinitions.Count != 8) return;
        var pitch = Math.Clamp((Chart.ActualWidth - 38) / activity.Weeks.Count, 14, 36);
        foreach (var row in Chart.RowDefinitions.Skip(1))
            if (Math.Abs(row.Height.Value - pitch) > 0.1) row.Height = new GridLength(pitch);
    }
}
