using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WordTrail.Core;
using WordTrail.Desktop.ViewModels;
using WordTrail.Desktop.Views;

namespace WordTrail.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public void ResponsiveDashboardKeepsActionsNearContentAndHeatmapFillsAvailableWidth()
    {
        OffscreenWpf.Invoke(() =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var store = new RecordingStore { Activity = Enumerable.Range(0, 365).Where(i => i % 4 != 0)
                .Select(i => new StudyActivity(today.AddDays(-i), i % 23 + 1)).ToArray() };
            var dashboard = new DashboardViewModel(store, () => { }, () => { });
            dashboard.LoadAsync().GetAwaiter().GetResult();
            var view = new DashboardView { DataContext = dashboard, FontSize = 14,
                FontFamily = new("Segoe UI, Microsoft JhengHei UI"), Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            double smallCellWidth = 0;
            foreach (var width in new[] { 715, 959, 1850 })
            {
                Layout(view, width, 1050);
                var start = Descendants(view).OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "StartReview");
                Assert.True(start.TranslatePoint(new Point(), view).X < 440);
                var heatmap = Assert.Single(Descendants(view).OfType<ActivityHeatmap>());
                Assert.True(heatmap.ActualWidth > width - 100);
                var cells = Descendants(heatmap).OfType<Button>().ToArray();
                Assert.Equal(365, cells.Length);
                if (width == 715) smallCellWidth = cells[0].ActualWidth;
                if (width == 1850) Assert.True(cells[0].ActualWidth > smallCellWidth * 2);
                var months = Descendants(heatmap).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "HeatmapMonth").ToArray();
                Assert.All(months, month =>
                {
                    Assert.Contains(month.Text, CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames);
                    var text = new FormattedText(month.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(month.FontFamily, month.FontStyle, month.FontWeight, month.FontStretch), month.FontSize, Brushes.Black, 1);
                    Assert.True(month.ActualWidth >= text.Width - 0.5);
                    AssertFits(heatmap, month);
                });
                Assert.Contains(Descendants(heatmap).OfType<TextBlock>(), x => x.Text == "Sun");
                if (width is 959 or 1850) Render(view, $"dashboard-v017-{width}.png");
            }
            var library = new LibraryView { DataContext = new LibraryViewModel(store, _ => { }), FontSize = 14 };
            Layout(library, 1850, 900);
            var add = Descendants(library).OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "NewWord");
            Assert.True(add.TranslatePoint(new Point(), library).X < 400);
        });
    }

    [Fact]
    public void ResponsiveHeatmapMonthLabelsDoNotOverlapAtPartialYearBoundaries()
    {
        OffscreenWpf.Invoke(() =>
        {
            foreach (var today in new[] { new DateOnly(2026, 10, 3), new DateOnly(2026, 1, 31), new DateOnly(2028, 2, 29) })
            {
                var heatmap = new ActivityHeatmap { Activity = LearningActivity.Build([], today) };
                Layout(heatmap, 640, 160);
                var bounds = Descendants(heatmap).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "HeatmapMonth")
                    .Select(x => x.TransformToAncestor(heatmap).TransformBounds(new Rect(0, 0, x.ActualWidth, x.ActualHeight))).OrderBy(x => x.Left).ToArray();
                for (var i = 1; i < bounds.Length; i++) Assert.True(bounds[i - 1].Right <= bounds[i].Left);
            }
        });
    }
}
