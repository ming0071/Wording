using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData("outlet", 1)]
    [InlineData("complimentary", 3)]
    [InlineData("be responsible for keeping track of customer orders", 1)]
    public void CompactRecallCentersTheWholePromptAndKeepsTheHeadwordCenteredAfterFlipping(string headword, int count)
    {
        OffscreenWpf.Invoke(() =>
        {
            var store = new RecordingStore { NextReview = new(ReviewColumnWord(count) with { Headword = headword }, 0, true, null) };
            var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
            var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, settings, new SilentSpeech());
            var model = new ReviewViewModel(store, new SilentSpeech(), settings, reading);
            var view = new ReviewView { DataContext = model, IsCompactLayout = true, FontSize = 14,
                FontFamily = new("Segoe UI, Microsoft JhengHei UI"), Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            foreach (var width in new[] { 715, 959 })
            {
                model.LoadAsync().GetAwaiter().GetResult(); reading.Ready(); Layout(view, width, 710);
                var card = (FrameworkElement)view.FindName("ReviewCard");
                var content = (FrameworkElement)view.FindName("ContentHost");
                Assert.True(content.ActualHeight < card.ActualHeight * 0.75);
                Assert.Equal(card.ActualHeight / 2, content.TranslatePoint(new Point(0, content.ActualHeight / 2), card).Y, 1);
                foreach (var answer in new[] { false, true })
                {
                    if (answer) { model.FlipCommand.Execute(null); Layout(view, width, 710); }
                    var word = (TextBox)view.FindName("HeadwordText");
                    Assert.Equal(card.ActualWidth / 2, word.TranslatePoint(new Point(word.ActualWidth / 2, 0), card).X, 1);
                    AssertFits(view, "ReviewHeadword"); AssertFits(view, "SpeakWord"); AssertFits(view, answer ? "RateGood" : "FlipCard");
                    Render(view, $"review-compact-centered-{count}-{headword.Length}-{width}-{answer}.png");
                }
            }
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void CompactReviewPreservesAlignedLearningRowsAndRestoresTheFullLayout(int count)
    {
        OffscreenWpf.Invoke(() =>
        {
            var baseWord = ReviewColumnWord(3);
            var word = baseWord with { Headword = count == 1 ? "be responsible for keeping track of customer orders" : "complimentary",
                AdditionalSenses = Enumerable.Range(1, count - 1).Select(i => baseWord.Definitions[i % 3] with { Id = Guid.NewGuid() }).ToArray() };
            var store = new RecordingStore { NextReview = new(word, 0, true, null), Categories = ["商務與法規", "會議與溝通"] };
            var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
            var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, settings, new SilentSpeech());
            var review = new ReviewViewModel(store, new SilentSpeech(), settings, reading);
            review.LoadAsync().GetAwaiter().GetResult(); review.FlipCommand.Execute(null); reading.Ready();
            review.Notice = "已保存。下一次複習：11/07 22:41";
            var view = new ReviewView { DataContext = review, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            foreach (var width in new[] { 715, 959, 1850 })
            {
                view.IsCompactLayout = false; Layout(view, width, 710);
                var fullPixels = ReviewPixels(view);
                var before = ((ScrollViewer)view.FindName("DefinitionScroll")).ViewportHeight;
                view.IsCompactLayout = true; Layout(view, width, 710);
                var scroll = (ScrollViewer)view.FindName("DefinitionScroll");
                if (count > 1) Assert.True(scroll.ViewportHeight > before + 60);
                Assert.Single(Descendants(view).OfType<FrameworkElement>().Where(IsShown), x => AutomationProperties.GetAutomationId(x) == "CompactReviewToolbar");
                AssertFits(view, "ReviewScopeButton"); AssertFits(view, "ReviewHeadword"); AssertFits(view, "SpeakExamples"); AssertFits(view, "RateGood");
                AssertFits(view, "WindowSizeHint");
                var keyboardHint = Descendants(view).OfType<TextBlock>().Single(x => AutomationProperties.GetAutomationId(x) == "ReviewKeyboardHint");
                var sizeHint = Descendants(view).OfType<TextBlock>().Single(x => AutomationProperties.GetAutomationId(x) == "WindowSizeHint");
                Assert.Equal(keyboardHint.FontSize, sizeHint.FontSize); Assert.Equal(keyboardHint.Foreground, sizeHint.Foreground);
                Assert.Equal(keyboardHint.TranslatePoint(new Point(), view).Y, sizeHint.TranslatePoint(new Point(), view).Y, 1);
                Assert.Equal(view.ActualWidth, sizeHint.TranslatePoint(new Point(sizeHint.ActualWidth, 0), view).X, 1);
                var columns = Descendants(view).OfType<Border>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewDefinition").ToArray();
                for (var start = 0; start < columns.Length; start += review.DefinitionColumns)
                {
                    var row = columns.Skip(start).Take(review.DefinitionColumns).ToArray();
                    foreach (var id in new[] { "ReviewEnglishDefinitionSection", "ReviewStudyDetails", "ReviewExamples" })
                    {
                        var tops = row.Select(column => Descendants(column).OfType<FrameworkElement>().Single(x => AutomationProperties.GetAutomationId(x) == id)
                            .TranslatePoint(new Point(), view).Y).ToArray();
                        Assert.All(tops, top => Assert.InRange(top, tops[0] - 0.2, tops[0] + 0.2));
                    }
                }
                Assert.All(Descendants(scroll).OfType<TextBlock>().Where(IsShown), text => AssertFitsReviewContent(view, text));
                Assert.All(Descendants(scroll).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewExampleEnglish"), text => Assert.Equal(23, text.FontSize));
                Assert.True(scroll.ScrollableWidth <= 0.1);
                Render(view, $"review-compact-aligned-{count}-{width}.png");
                view.IsCompactLayout = false; Layout(view, width, 710);
                Assert.Equal(fullPixels, ReviewPixels(view));
            }
        });
    }

    [Fact]
    public void ShellUsesCompactReviewOnlyWhenReviewingInAShortWindow()
    {
        OffscreenWpf.Invoke(() =>
        {
            var main = new MainViewModel(new RecordingStore { NextReview = new(ReviewColumnWord(3), 0, true, null) }, new UnusedBackup(),
                new FixedGenerator(), new SilentSpeech(), new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false }, new(), Path.GetTempPath());
            var window = new MainWindow { DataContext = main };
            var root = (FrameworkElement)window.Content;
            Layout(root, 1240, 820); Assert.False(window.IsCompactReview);
            main.NavigateCommand.Execute("review"); Layout(root, 1240, 820);
            Assert.True(window.IsCompactReview);
            var view = Assert.Single(Descendants(root).OfType<ReviewView>()); Assert.True(view.IsCompactLayout);
            main.Review.FlipCommand.Execute(null); Layout(root, 1240, 820);
            Render(root, "review-shell-compact.png");
            Layout(root, 2000, 1100); Assert.False(window.IsCompactReview); Assert.False(view.IsCompactLayout);
            Render(root, "review-shell-full.png");
            Layout(root, 1240, 820); Assert.True(window.IsCompactReview);
            main.NavigateCommand.Execute("today"); Layout(root, 1240, 820);
            Assert.False(window.IsCompactReview);
            window.Content = null; window.Close();
        });
    }

    [Fact]
    public async Task CompactReviewSummariesFollowCategorySelectionAndReviewProgress()
    {
        var store = new RecordingStore { Categories = ["商務與法規", "會議與溝通"], NextReview = new(ReviewColumnWord(1), 0, true, null) };
        var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false });
        await review.LoadAsync(); Assert.Equal("全部單字庫", review.ScopeButtonText);
        review.CategoryScope.SetSelected(store.Categories); await review.ApplyCategoryCommand.ExecuteAsync();
        Assert.Equal("商務與法規 + 1 類", review.ScopeButtonText); Assert.Contains("到期", review.ScopeCountsText);
        review.CategoryScope.SetSelected([]); await review.ApplyCategoryCommand.ExecuteAsync();
        Assert.Equal("全部單字庫", review.ScopeButtonText);
        store.Submit = submission => Task.FromResult(new ReviewResult(submission.OperationId, DateTimeOffset.UtcNow.AddDays(1), "Review"));
        review.FlipCommand.Execute(null); await review.GoodCommand.ExecuteAsync();
        Assert.Equal("1 已複習", review.CompactProgressText);
        review.BeginSession(); Assert.Equal("0 已複習", review.CompactProgressText);
        OffscreenWpf.Invoke(() => Assert.False(ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false, new ToggleButton())));
    }

    private static byte[] ReviewPixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
}
