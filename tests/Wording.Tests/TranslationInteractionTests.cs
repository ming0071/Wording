using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task TranslationNavigationPreservesAnswersAndResetClearsTheSession()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var main = new MainViewModel(data.Store, new BackupService(data.Store), new TranslationFixtures.Generator(),
                new PracticeFixtures.Speech(), new(), new(), data.DirectoryPath);
            var page = Assert.IsType<TranslationViewModel>(main.Translation);
            main.NavigateCommand.Execute("translation");
            for (var i = 0; i < 100 && main.CurrentPage != page; i++) await Task.Delay(10);
            Assert.Same(page, main.CurrentPage); Assert.True(main.IsTranslationSelected);
            await page.GenerateCommand.ExecuteAsync(); Assert.Empty(page.Error);
            page.Questions[0].Answer = "Please keep my answer.";
            var questions = page.Questions;
            main.NavigateCommand.Execute("library");
            for (var i = 0; i < 100 && main.CurrentPage != main.Library; i++) await Task.Delay(10);
            await Task.Delay(30);
            main.NavigateCommand.Execute("translation");
            for (var i = 0; i < 100 && main.CurrentPage != page; i++) await Task.Delay(10);
            Assert.Same(page, main.CurrentPage);
            Assert.Same(questions, page.Questions); Assert.Equal("Please keep my answer.", page.Questions[0].Answer);
            var window = new MainWindow { DataContext = main };
            Layout((FrameworkElement)window.Content, 1000, 700);
            Assert.Contains(Descendants((FrameworkElement)window.Content).OfType<Button>(), x => AutomationProperties.GetAutomationId(x) == "NavTranslation");
            window.Close();
            page.Reset(); Assert.False(page.HasMaterial);
        });
    }

    [Fact]
    public async Task TranslationViewRendersInputAndFeedbackAtMinimumSizeWithoutRevealingAnswersEarly()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        await data.AddWordAsync(); await data.AddWordAsync("invoice"); await data.AddWordAsync("confirm");
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new TranslationViewModel(data.Store, new TranslationFixtures.Generator(), () => true, data.Clock);
            var view = new TranslationView { DataContext = vm, FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(view, 715, 560); AssertFits(view, "GenerateTranslation");
            await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Layout(view, 715, 560);
            AssertFits(view, "SubmitTranslation");
            var answers = Descendants(view).OfType<TextBox>().Where(x => AutomationProperties.GetAutomationId(x) == "TranslationAnswer").ToArray();
            Assert.Equal(3, answers.Length);
            Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(), x => x.Text.StartsWith("Please confirm") && IsShown(x));
            answers[0].Text = "Please confirm the appointment today.";
            Assert.Equal(answers[0].Text, vm.Questions[0].Answer);
            foreach (var question in vm.Questions.Skip(1)) question.Answer = "Please confirm the details today.";
            Layout(view, 715, 560);
            var scroll = (ScrollViewer)view.FindName("ExerciseScroll");
            scroll.ScrollToBottom(); Layout(view, 715, 560);
            var offset = scroll.VerticalOffset;
            vm.Questions[^1].Answer = "Please confirm the invoice today.";
            Layout(view, 715, 560); Assert.Equal(offset, scroll.VerticalOffset);
            scroll.ScrollToTop(); Layout(view, 715, 560);
            Render(view, "translation-questions-minimum.png");
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Layout(view, 715, 560);
            var firstVerdict = Descendants(view).OfType<TextBlock>().First(x => AutomationProperties.GetAutomationId(x) == "TranslationVerdict");
            var verdictTop = firstVerdict.TranslatePoint(new Point(), scroll).Y;
            Assert.InRange(verdictTop, 0, scroll.ViewportHeight - firstVerdict.ActualHeight);
            var firstSuggestion = Descendants(view).OfType<TextBlock>().First(x => AutomationProperties.GetAutomationId(x) == "TranslationSuggestion");
            Assert.InRange(firstSuggestion.TranslatePoint(new Point(), scroll).Y, 0, scroll.ViewportHeight - firstSuggestion.ActualHeight);
            Assert.True(scroll.VerticalOffset > 0);
            Assert.Equal(1, vm.FeedbackRevision);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), x => x.Text == "意思表達完整。" && IsShown(x));
            AssertFits(view, "SubmitTranslation");
            Assert.All(Descendants(view).OfType<ScrollViewer>(), x => Assert.True(x.ScrollableWidth <= 0.1));
            Render(view, "translation-feedback-minimum.png");
            Layout(view, 975, 700); Render(view, "translation-feedback-wide.png");
            scroll.ScrollToBottom(); Layout(view, 715, 560);
            var editedOffset = scroll.VerticalOffset;
            vm.Questions[^1].Answer = "I changed the final answer.";
            Layout(view, 715, 560);
            Assert.Equal(Math.Min(editedOffset, scroll.ScrollableHeight), scroll.VerticalOffset);
            Assert.Equal(1, vm.FeedbackRevision);
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Layout(view, 715, 560);
            Assert.Equal(2, vm.FeedbackRevision);
            Assert.InRange(firstVerdict.TranslatePoint(new Point(), scroll).Y, 0, scroll.ViewportHeight - firstVerdict.ActualHeight);
        });
    }
}
