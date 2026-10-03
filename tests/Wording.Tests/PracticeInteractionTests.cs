using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task PracticeNavigationKeepsUnsubmittedWorkAndStopsAudio()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var speech = new PracticeFixtures.Speech();
            var main = new MainViewModel(data.Store, new UnusedBackup(), new PracticeFixtures.Generator(), speech,
                new AppSettings { Practice = new() { Mode = PracticeMode.Listening } }, new(), data.DirectoryPath);
            async Task Navigate(string section, PageViewModel expected)
            {
                main.NavigateCommand.Execute(section);
                // Navigation loads asynchronously on the same dispatcher.
                for (var i = 0; i < 100 && (main.CurrentPage != expected || main.Practice!.Topics.Choices.Count == 1); i++) await Task.Delay(10);
                Assert.Same(expected, main.CurrentPage);
                await Task.Delay(20);
            }
            await Navigate("practice", main.Practice!);
            await main.Practice!.GenerateCommand.ExecuteAsync(); Assert.Empty(main.Practice.Error);
            main.Practice.Questions[0].SelectedIndex = 2;
            var questions = main.Practice.Questions;
            main.Practice.PlayCommand.Execute(null);
            await Navigate("library", main.Library); Assert.Equal(PlaybackState.Stopped, speech.Playback);
            await Navigate("practice", main.Practice);
            Assert.Same(questions, main.Practice.Questions); Assert.Equal(2, main.Practice.Questions[0].SelectedIndex);
            Assert.Empty(main.Practice.Passage); Assert.False(main.Practice.IsSubmitted);
        });
    }

    [Fact]
    public async Task PracticeHidesOptionsAfterGenerationAndReconfigurationKeepsWorkUntilReplacementIsConfirmed()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var generator = new PracticeFixtures.Generator();
            var allowReplace = false; var confirmations = 0;
            var vm = new PracticeViewModel(data.Store, data.Store, generator, new PracticeFixtures.Speech(), new(), () => { }, _ => { },
                () => { confirmations++; return allowReplace; }, data.Clock);
            await vm.LoadAsync();
            var view = new PracticeView { DataContext = vm, FontSize = 14 };
            var setup = (Border)view.FindName("SetupPane");
            var page = (ScrollViewer)view.FindName("PageScroll");
            Layout(view, 715, 560); Assert.True(IsShown(setup));
            page.ScrollToBottom(); Layout(view, 715, 560); Assert.True(page.VerticalOffset > 0);
            await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Layout(view, 715, 560);
            Assert.False(IsShown(setup)); Assert.False(vm.IsChoosingOptions); Assert.Equal(0, page.VerticalOffset);
            AssertFits(view, "ConfigurePractice");
            vm.Questions[0].SelectedIndex = 2; var questions = vm.Questions;
            var passage = vm.Passage;
            vm.ToggleOptionsCommand.Execute(null); Layout(view, 715, 560);
            Assert.True(IsShown(setup)); Assert.Equal("返回當次練習", vm.OptionsActionText);
            Assert.Same(questions, vm.Questions); Assert.Equal(2, questions[0].SelectedIndex); Assert.Equal(0, confirmations);
            vm.ToggleOptionsCommand.Execute(null); Layout(view, 715, 560);
            Assert.False(IsShown(setup)); Assert.Same(questions, vm.Questions);
            vm.ListeningCommand.Execute(null); Layout(view, 715, 560);
            Assert.Equal(1, confirmations); Assert.True(vm.IsReading); Assert.False(IsShown(setup));
            Assert.Same(questions, vm.Questions); Assert.Equal(passage, vm.Passage); Assert.Equal(2, questions[0].SelectedIndex);
            allowReplace = true;
            vm.ToggleOptionsCommand.Execute(null); generator.Fail = true;
            await vm.GenerateCommand.ExecuteAsync(); Assert.NotEmpty(vm.Error); Layout(view, 715, 560);
            Assert.True(IsShown(setup)); Assert.Same(questions, vm.Questions); Assert.Equal(2, questions[0].SelectedIndex);
            vm.ListeningCommand.Execute(null); Layout(view, 715, 560);
            Assert.True(vm.IsListening); Assert.True(IsShown(setup)); Assert.False(vm.HasMaterial);
            generator.Fail = false;
            await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error); Layout(view, 715, 560);
            Assert.False(IsShown(setup)); Assert.False(vm.IsChoosingOptions);
            foreach (var question in vm.Questions) question.SelectedIndex = 0;
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error); Layout(view, 715, 560);
            Assert.False(IsShown(setup));
            vm.Reset(); Layout(view, 715, 560); Assert.True(IsShown(setup));
        });
    }

    [Fact]
    public async Task PracticePageRendersAtMinimumSizeAndHidesListeningTranscriptUntilSubmission()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), new(), () => { }, _ => { }, () => true, data.Clock);
            await vm.LoadAsync();
            var view = new PracticeView { DataContext = vm, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(view, 715, 560); Render(view, "practice-setup-minimum.png");
            Assert.Equal(PracticeLevel.Medium, vm.Difficulty.Value); Assert.Equal(WordDensity.Medium, vm.Density.Value);
            Assert.All(Descendants(view).OfType<ScrollViewer>(), x => Assert.True(x.ScrollableWidth <= 0.1));
            var heatmap = (ItemsControl)view.FindName("TopicHeatmap");
            Assert.Equal(2, heatmap.Items.Count);
            Assert.All(vm.TopicActivities, x => Assert.Equal(0, x.Count));
            Assert.DoesNotContain(Descendants(view), x => x is ActivityHeatmap);
            await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            ScrollPracticeTo(view, "ExerciseWorkspace", 715, 560); Render(view, "practice-reading-minimum.png");
            Assert.Contains(Descendants(view).OfType<TextBlock>(), x => x.Text == vm.Passage && IsShown(x));
            var article = (ScrollViewer)view.FindName("ArticleScroll");
            var questionScroll = (ScrollViewer)view.FindName("QuestionScroll");
            var articlePane = (Border)view.FindName("ArticlePane");
            var questionPane = (Border)view.FindName("QuestionPane");
            var articleBounds = articlePane.TransformToAncestor(view).TransformBounds(new Rect(articlePane.RenderSize));
            var questionBounds = questionPane.TransformToAncestor(view).TransformBounds(new Rect(questionPane.RenderSize));
            Assert.True(articleBounds.Right < questionBounds.Left);
            Assert.Equal(articleBounds.Top, questionBounds.Top);
            AssertFits(view, "SubmitPractice");
            questionScroll.ScrollToBottom(); Layout(view, 715, 560);
            Assert.True(questionScroll.VerticalOffset > 0); Assert.Equal(0, article.VerticalOffset);
            var questionOffset = questionScroll.VerticalOffset;
            article.ScrollToBottom(); Layout(view, 715, 560);
            Assert.True(article.VerticalOffset > 0); Assert.Equal(questionOffset, questionScroll.VerticalOffset);
            article.ScrollToTop(); questionScroll.ScrollToTop();
            ScrollPracticeTo(view, "ExerciseWorkspace", 959, 710); Render(view, "practice-reading-side-by-side.png");
            Assert.All(vm.Questions, x => Assert.Empty(x.Explanation));
            vm.ListeningCommand.Execute(null); await vm.GenerateCommand.ExecuteAsync();
            ScrollPracticeTo(view, "ExerciseWorkspace", 715, 560); Render(view, "practice-listening-minimum.png");
            Assert.Empty(vm.Passage);
            Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(), x => x.Text.Contains("Please confirm your appointment"));
            var questions = Descendants(view).OfType<ListBox>().Where(x => x.DataContext is PracticeQuestionViewModel).ToArray();
            Assert.Equal(4, questions.Length);
            foreach (var question in questions) question.SelectedIndex = 0;
            Assert.True(vm.SubmitCommand.CanExecute(null));
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            ScrollPracticeTo(view, "ExerciseWorkspace", 715, 560);
            Assert.NotEmpty(vm.Passage); Assert.True(vm.IsSubmitted);
            Assert.False(Descendants(view).OfType<Expander>().Single(x => x.Header as string == "展開全文中文翻譯").IsExpanded);
            Assert.All(vm.Questions, x => Assert.False(x.CanAnswer));
            questionScroll.ScrollToBottom(); Layout(view, 715, 560); Render(view, "practice-results-minimum.png");
            Assert.All(Descendants(view).OfType<ScrollViewer>(), x => Assert.True(x.ScrollableWidth <= 0.1));
            Assert.Equal(1, vm.CompletedCount);
            Assert.Equal(1, vm.TopicActivities.Sum(x => x.Count));
            vm.Reset(); Assert.False(vm.HasMaterial);
        });
    }

    [Fact]
    public async Task PracticeMouseWheelOverAnswersScrollsQuestionsWithoutChangingAnswersOrArticle()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), new(), () => { }, _ => { }, () => true, data.Clock);
            await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            var view = new PracticeView { DataContext = vm, FontSize = 14 };
            ScrollPracticeTo(view, "ExerciseWorkspace", 715, 560);
            var article = (ScrollViewer)view.FindName("ArticleScroll");
            var questions = (ScrollViewer)view.FindName("QuestionScroll");
            var page = (ScrollViewer)view.FindName("PageScroll");
            var pageOffset = page.VerticalOffset;
            var answers = Descendants(questions).OfType<ListBox>().ToArray();
            Assert.Equal(4, answers.Length);
            answers[0].SelectedIndex = 2;
            var option = Descendants(answers[0]).OfType<TextBlock>().First();
            void Wheel(UIElement source, int delta)
            {
                // Deliver the same tunnel/bubble pair as WPF mouse input, from the hovered child.
                var input = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                source.RaiseEvent(input);
                input.RoutedEvent = UIElement.MouseWheelEvent;
                source.RaiseEvent(input);
                Layout(view, 715, 560);
            }
            Wheel(option, -120);
            Assert.True(questions.VerticalOffset > 0, "The wheel over an answer must scroll the question pane.");
            Assert.Equal(2, vm.Questions[0].SelectedIndex);
            Assert.Equal(0, article.VerticalOffset);
            Assert.Equal(pageOffset, page.VerticalOffset);
            var offset = questions.VerticalOffset;
            Wheel(option, 120);
            Assert.True(questions.VerticalOffset < offset);
            questions.ScrollToBottom(); Layout(view, 715, 560);
            Wheel(option, -120);
            Assert.Equal(questions.ScrollableHeight, questions.VerticalOffset);
            Assert.Equal(0, article.VerticalOffset);
            Assert.True(page.VerticalOffset > pageOffset, "At the question boundary, the wheel must continue down the page.");
            pageOffset = page.VerticalOffset;
            questions.ScrollToTop(); Layout(view, 715, 560);
            Wheel(option, 120);
            Assert.Equal(0, questions.VerticalOffset);
            Assert.True(page.VerticalOffset < pageOffset);

            foreach (var answer in answers) answer.SelectedIndex = 0;
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            questions.ScrollToTop(); Layout(view, 715, 560);
            Assert.False(answers[0].IsEnabled);
            Wheel(option, -120);
            Assert.True(questions.VerticalOffset > 0);
            var explanation = Descendants(questions).OfType<TextBlock>().First(x => x.Text == vm.Questions[0].Explanation);
            offset = questions.VerticalOffset;
            Wheel(explanation, -120);
            Assert.True(questions.VerticalOffset > offset);
            Assert.All(vm.Questions, x => Assert.Equal(0, x.SelectedIndex));
            Assert.Equal(0, article.VerticalOffset);
        });
    }

    [Fact]
    public async Task PracticeSetupAndHistoryDisplayVerticallyAndWholePageScrollsWithoutExpanders()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        for (var i = 0; i < 24; i++) await data.Store.AddCategoryAsync($"主題 {i:00}");
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), new(), () => { }, _ => { }, () => true, data.Clock);
            await vm.LoadAsync();
            var view = new PracticeView { DataContext = vm, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(view, 715, 560);
            var scroll = (ScrollViewer)view.FindName("PageScroll");
            AssertFits(view, scroll); Assert.True(scroll.ScrollableHeight > 0);
            var setup = (Border)view.FindName("SetupPane");
            var history = (Border)view.FindName("HistoryPane");
            Assert.Null(view.FindName("HistoryExpander"));
            Assert.DoesNotContain(Descendants(setup), x => x is Expander or ScrollViewer);
            Assert.DoesNotContain(Descendants(history), x => x is Expander or ScrollViewer);
            Assert.Equal(setup.ActualWidth, history.ActualWidth, 1);
            Assert.True(setup.TransformToAncestor(view).Transform(new Point(0, setup.ActualHeight)).Y
                < history.TransformToAncestor(view).Transform(new Point()).Y);
            var difficulty = Descendants(setup).OfType<System.Windows.Controls.Primitives.ToggleButton>()
                .Single(x => ReferenceEquals(x.DataContext, vm.Difficulty.Choices.Single(c => c.IsSelected)));
            difficulty.BringIntoView(); Layout(view, 715, 560);
            AssertFits(view, difficulty);
            var offset = scroll.VerticalOffset;
            difficulty.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            Layout(view, 715, 560); Assert.True(scroll.VerticalOffset > offset);
            Assert.Equal(PracticeLevel.Medium, vm.Difficulty.Value);
            ScrollPracticeTo(view, "HistoryPane", 715, 560);
            var tile = Descendants(history).OfType<Border>().First(x => Equals(x.DataContext, vm.TopicActivities[0]));
            AssertFits(view, tile);
            var tileCenter = tile.TransformToAncestor(view).Transform(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
            Assert.True(Assert.IsAssignableFrom<Visual>(VisualTreeHelper.HitTest(view, tileCenter)?.VisualHit).IsDescendantOf(history));
            offset = scroll.VerticalOffset;
            tile.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            Layout(view, 715, 560); Assert.True(scroll.VerticalOffset > offset);
            scroll.ScrollToBottom(); Layout(view, 715, 560);
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
            var last = Descendants(history).OfType<Border>().First(x => Equals(x.DataContext, vm.TopicActivities[^1]));
            AssertFits(view, last);
            Render(view, "practice-topics-scroll-minimum.png");
            await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            ScrollPracticeTo(view, "ExerciseWorkspace", 715, 560);
            AssertFits(view, "SubmitPractice");
            AssertFits(view, (Border)view.FindName("ArticlePane"));
            Assert.DoesNotContain(Descendants(setup), x => x is Expander);
            Assert.DoesNotContain(Descendants(history), x => x is Expander);
            Assert.Equal(26, ((ItemsControl)view.FindName("TopicHeatmap")).Items.Count);
            Assert.DoesNotContain(Descendants(view), x => x is ActivityHeatmap);
        });
    }

    [Fact]
    public async Task PracticeTopicHeatmapShowsEightTopicsWithVisibleCountsAndDifferentIntensities()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        var counts = new Dictionary<string, int> { ["商業"] = 7, ["旅行"] = 2, ["職場與人事"] = 4,
            ["會議與溝通"] = 0, ["產品與物流"] = 5, ["財務與投資"] = 3, ["餐飲與購物"] = 1, ["服務與活動"] = 0 };
        foreach (var (topic, count) in counts)
        {
            await data.Store.AddCategoryAsync(topic);
            for (var i = 0; i < count; i++) await data.Store.CompletePracticeAsync(new(Guid.NewGuid(), data.Clock.Now.AddDays(-i),
                i % 2 == 0 ? PracticeMode.Reading : PracticeMode.Listening, [topic], [word.Id]));
        }
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), new(), () => { }, _ => { }, () => true, data.Clock);
            await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync();
            var view = new PracticeView { DataContext = vm, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            ScrollPracticeTo(view, "HistoryPane", 959, 710); Render(view, "practice-topic-heatmap.png");
            Assert.Equal(8, ((ItemsControl)view.FindName("TopicHeatmap")).Items.Count);
            foreach (var activity in vm.TopicActivities)
            {
                Assert.Contains(Descendants(view).OfType<TextBlock>(), x => Equals(x.DataContext, activity) && x.Text == activity.Name);
                Assert.Contains(Descendants(view).OfType<TextBlock>(), x => Equals(x.DataContext, activity) && x.Text == $"{counts[activity.Name]} 次");
            }
            AssertFits(view, (ScrollViewer)view.FindName("PageScroll"));
            ScrollPracticeTo(view, "HistoryPane", 715, 560); Render(view, "practice-topic-heatmap-minimum.png");
            AssertFits(view, (ScrollViewer)view.FindName("PageScroll"));
            Assert.All(Descendants(view).OfType<ScrollViewer>(), x => Assert.True(x.ScrollableWidth <= 0.1));
        });
    }

    [Fact]
    public async Task PracticeNewWordRatingsRemainVisibleWithQuotaReasonAndReenableAfterLimitChange()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        await data.AddWordAsync(); await data.Store.SaveVocabularyAsync(StudyTestData.Word("office", "辦公室"));
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var settings = new AppSettings { DailyNewLimit = 1 };
            var openedSettings = false;
            var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), settings,
                () => { }, _ => { }, () => true, data.Clock, () => openedSettings = true);
            await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync();
            foreach (var question in vm.Questions) question.SelectedIndex = 0;
            await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Assert.All(vm.Targets, x => Assert.True(x.GoodCommand.CanExecute(null)));
            var first = vm.Targets[0]; var second = vm.Targets[1];
            await first.GoodCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Assert.True(second.IsNewLimitBlocked); Assert.True(second.CanOfferRating);
            Assert.Contains("已開始 1 個／今日上限 1 個", second.Status);
            Assert.False(second.GoodCommand.CanExecute(null));
            var view = new PracticeView { DataContext = vm, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(view, 959, 710);
            var button = Descendants(view).OfType<Button>().Single(x => x.DataContext == second && x.Content as string == "3 · 原本就懂");
            Assert.True(IsShown(button)); Assert.False(button.IsEnabled);
            var link = Descendants(view).OfType<Button>().Single(x => x.DataContext == second && x.Content as string == "查看每日新詞設定");
            link.Command.Execute(null); Assert.True(openedSettings);
            var status = Descendants(view).OfType<TextBlock>().Single(x => x.DataContext == second && x.Text == second.Status);
            status.BringIntoView(); Layout(view, 959, 710);
            ((ScrollViewer)view.FindName("QuestionScroll")).ScrollToBottom(); Layout(view, 959, 710); Render(view, "practice-new-word-quota.png");
            Assert.All(Descendants(view).OfType<ScrollViewer>(), x => Assert.True(x.ScrollableWidth <= 0.1));
            settings.DailyNewLimit = 2; await vm.RefreshCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Assert.False(second.IsNewLimitBlocked); Assert.True(second.GoodCommand.CanExecute(null));
            Layout(view, 959, 710); Assert.True(button.IsEnabled);
            await second.GoodCommand.ExecuteAsync(); Assert.Empty(vm.Error);
            Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        });
    }

    private static void ScrollPracticeTo(PracticeView view, string section, double width, double height)
    {
        Layout(view, width, height);
        var page = (ScrollViewer)view.FindName("PageScroll");
        var element = (FrameworkElement)view.FindName(section);
        page.ScrollToVerticalOffset(page.VerticalOffset + element.TransformToAncestor(page).Transform(new Point()).Y);
        Layout(view, width, height);
    }
}
