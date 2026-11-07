using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public void ReadingStatesShowRecoveryActionOnlyAfterFailureAndFitNarrowColumns()
    {
        OffscreenWpf.Invoke(() =>
        {
            var settingsOpened = false;
            var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
            var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, settings, new SilentSpeech(), () => settingsOpened = true);
            var store = new RecordingStore { NextReview = new(ReviewColumnWord(2), 0, true, null) };
            var model = new ReviewViewModel(store, new SilentSpeech(), settings, reading);
            model.LoadAsync().GetAwaiter().GetResult(); model.FlipCommand.Execute(null);
            var view = new ReviewView { DataContext = model, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            void Check(string expected)
            {
                Layout(view, 959, 710);
                var state = Assert.Single(Descendants(view).OfType<TextBlock>().Where(IsShown), x => AutomationProperties.GetAutomationId(x) == "ReadingState");
                Assert.Equal(expected, state.Text);
                AssertFitsReviewContent(view, state);
                ShowReading(959);
            }
            void ShowReading(int width)
            {
                var scroll = (ScrollViewer)view.FindName("DefinitionScroll");
                var state = Descendants(view).OfType<TextBlock>().Single(x => IsShown(x) && AutomationProperties.GetAutomationId(x) == "ReadingState");
                scroll.ScrollToVerticalOffset(Math.Max(0, state.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 36));
                Layout(view, width, 710);
            }
            reading.Connecting(); Check("連線中");
            reading.Ready(); Check("可錄音");
            Assert.DoesNotContain(Descendants(view).OfType<Button>().Where(IsShown), x => AutomationProperties.GetAutomationId(x) == "ReadingOpenSettings");
            Render(view, "reading-state-ready.png");
            reading.RecordCommand.Execute(null); reading.Recording(reading.Run); Check("收音中");
            Render(view, "reading-state-recording.png");
            reading.RecordCommand.Execute(null); Check("處理中");
            reading.Complete(reading.Run, model.Definitions[0].Examples[0].English); Check("比對完成");
            reading.Fail("找不到設定的 Codex 執行檔，請在設定頁按「自動尋找」或重新選擇檔案。"); Check("需要處理");
            foreach (var width in new[] { 715, 959, 1850 })
            {
                Layout(view, width, 710);
                var repair = Assert.Single(Descendants(view).OfType<Button>().Where(IsShown), x => AutomationProperties.GetAutomationId(x) == "ReadingOpenSettings");
                Assert.True(repair.IsEnabled); AssertFitsReviewContent(view, repair);
                repair.Command.Execute(null); Assert.True(settingsOpened);
                Assert.All(Descendants(view).OfType<TextBlock>().Where(IsShown), text => AssertFitsReviewContent(view, text));
                ShowReading(width);
                Render(view, $"reading-state-error-{width}.png");
            }
            reading.Connecting(); reading.Ready(); Check("可錄音");
            Assert.DoesNotContain(Descendants(view).OfType<Button>().Where(IsShown), x => AutomationProperties.GetAutomationId(x) == "ReadingOpenSettings");
        });
    }
}
