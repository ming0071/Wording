using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReviewContentIsCenteredBeforeAndAfterFlippingWithoutShrinking(int count)
    {
        OffscreenWpf.Invoke(() =>
        {
            var store = new RecordingStore { NextReview = new(ReviewColumnWord(count), 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false });
            review.LoadAsync().GetAwaiter().GetResult();
            var view = new ReviewView { DataContext = review, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            foreach (var width in new[] { 715, 959, 1850 })
            {
                var height = width == 715 ? 560 : width == 959 ? 710 : 900;
                store.NextReview = new(ReviewColumnWord(count), 0, true, null);
                review.LoadAsync().GetAwaiter().GetResult();
                Layout(view, width, height);
                AssertCenteredReview(view);
                AssertFits(view, "FlipCard");
                var header = Assert.Single(Descendants(view).OfType<FrameworkElement>(), x => AutomationProperties.GetAutomationId(x) == "ReviewHeader");
                var prompt = Assert.Single(Descendants(view).OfType<FrameworkElement>(), x => AutomationProperties.GetAutomationId(x) == "ReviewRecallPrompt");
                var headerBottom = header.TranslatePoint(new Point(0, header.ActualHeight), view).Y;
                Assert.InRange(prompt.TranslatePoint(new Point(), view).Y - headerBottom, 15, 17);
                Assert.DoesNotContain(Descendants(view).OfType<TextBlock>().Where(IsShown), x => x.Text == "本次依提示回想這個詞義");
                if (count == 1) Assert.Empty(review.SenseContext);
                else Assert.Contains($"{count} 組解釋", review.SenseContext);
                Render(view, $"review-v045-question-{count}-{width}.png");
                review.FlipCommand.Execute(null);
                Layout(view, width, height);
                AssertCenteredReview(view);
                var scroll = (ScrollViewer)view.FindName("DefinitionScroll");
                if (width == 1850) Assert.True(scroll.ScrollableHeight <= 0.1);
                if (width == 715 && count > 1) Assert.True(scroll.ScrollableHeight > 0);
                var headerTop = header.TranslatePoint(new Point(), view).Y;
                scroll.ScrollToBottom();
                Layout(view, width, height);
                Assert.Equal(headerTop, header.TranslatePoint(new Point(), view).Y, 1);
                AssertFits(view, "RateGood");
                AssertFits(view, "SpeakExamples");
                Assert.All(Descendants(scroll).OfType<TextBlock>().Where(IsShown), x => AssertFitsReviewContent(view, x));
                scroll.ScrollToTop();
                Layout(view, width, height);
                Render(view, $"review-v045-answer-{count}-{width}.png");
            }
        });
    }

    private static void AssertCenteredReview(ReviewView view)
    {
        var content = (FrameworkElement)view.FindName("ContentHost");
        var card = (FrameworkElement)VisualTreeHelper.GetParent(content);
        var position = content.TranslatePoint(new Point(), card);
        Assert.InRange(position.Y + content.ActualHeight / 2, card.ActualHeight / 2 - 1, card.ActualHeight / 2 + 1);
        Assert.InRange(position.Y, 13, card.ActualHeight / 2);
        AssertFits(view, "ReviewHeadword");
        AssertFits(view, "SpeakWord");
    }
}
