using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WordTrail.Core;
using WordTrail.Desktop;
using WordTrail.Desktop.ViewModels;
using WordTrail.Desktop.Views;
using WordTrail.Infrastructure;

namespace WordTrail.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task LearningFlowFlipAutomaticallySpeaksExamplesOnceAndSettingCanDisableIt()
    {
        var word = StudyTestData.Word() with { Examples = [new("First sentence.", "第一句"), new("Second sentence.", "第二句")] };
        var store = new RecordingStore { NextReview = new(word, 0, true, null) };
        var speech = new RecordingSpeech();
        var settings = new AppSettings { AutoSpeakWord = false };
        var review = new ReviewViewModel(store, speech, settings);
        await review.LoadAsync();
        ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false);
        Assert.Equal("First sentence. Second sentence.", Assert.Single(speech.Spoken));
        ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, true);
        ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false);
        Assert.Single(speech.Spoken);
        settings.AutoSpeakExamples = false;
        await review.LoadAsync();
        review.FlipCommand.Execute(null);
        Assert.Single(speech.Spoken);
        review.SpeakExampleCommand.Execute(null);
        Assert.Equal(2, speech.Spoken.Count);
    }

    [Fact]
    public async Task LearningFlowEditorCategoryMenuAddsRemovesAndPersistsMultipleTopics()
    {
        var store = new RecordingStore { Categories = ["旅行", "商業"] };
        var editor = new EditorViewModel(store, new FixedGenerator(), StudyTestData.Word(), _ => { });
        await editor.LoadAsync();
        editor.CategoriesText = "";
        editor.CategoryDraft = "旅行";
        editor.AddCategorySelectionCommand.Execute(null);
        editor.CategoryDraft = "旅行";
        editor.AddCategorySelectionCommand.Execute(null);
        Assert.Single(editor.SelectedCategories);
        editor.CategoryDraft = "新主題";
        editor.AddCategorySelectionCommand.Execute(null);
        editor.RemoveCategorySelectionCommand.Execute("旅行");
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal("新主題", Assert.Single(store.Saved!.Categories));
    }

    [Fact]
    public void LearningFlowDashboardEntryRoutesKeyboardThroughTheReviewPage()
    {
        OffscreenWpf.Invoke(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new RecordingStore { NextReview = new(StudyTestData.Word(), 0, true, null) };
            ReviewSubmission? rated = null;
            store.Submit = submission => { rated = submission; store.NextReview = null; return Task.FromResult(new ReviewResult(submission.OperationId, DateTimeOffset.UtcNow.AddDays(1), "Review")); };
            var main = new MainViewModel(store, new UnusedBackup(), new FixedGenerator(), new SilentSpeech(), new AppSettings(), new AiSettings(), Path.GetTempPath());
            var window = new MainWindow { DataContext = main };
            main.Dashboard.StartReviewCommand.Execute(null);
            Assert.Same(main.Review, main.CurrentPage);
            var review = new ReviewView { DataContext = main.Review };
            window.Content = review;
            Assert.True(review.Focusable);
            using var source = new HwndSource(new HwndSourceParameters("WordTrail invisible keyboard test") { Width = 1, Height = 1, WindowStyle = 0 });
            var flip = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            review.RaiseEvent(flip);
            Assert.True(flip.Handled);
            Assert.True(main.Review.IsAnswerVisible);
            var rate = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.D3) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            review.RaiseEvent(rate);
            Assert.Equal(ReviewRating.Good, rated!.Rating);
            window.Close();
        });
    }

    [Fact]
    public void LearningFlowRendersHeatmapChipsLargerExamplesAndWideDictionary()
    {
        OffscreenWpf.Invoke(() =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var word = StudyTestData.Word("bank", "銀行") with { Examples = [new("Please transfer the money to my bank account.", "請把款項轉到我的銀行帳戶。"), new("The bank closes at five in the afternoon.", "銀行下午五點關門。")], Synonyms = ["financial institution"], Notes = "和 river bank 不同意思。" };
            var store = new RecordingStore { NextReview = new(word, 0, true, null), Categories = ["旅行", "商業", "日常"],
                Activity = Enumerable.Range(0, 365).Where(i => i % 3 != 0).Select(i => new StudyActivity(today.AddDays(-i), i % 25 + 1)).ToArray() };
            var dashboard = new DashboardViewModel(store, () => { }, () => { });
            dashboard.LoadAsync().GetAwaiter().GetResult();
            var dashboardView = new DashboardView { DataContext = dashboard, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(dashboardView, 959, 710); Render(dashboardView, "dashboard-v016.png");
            Assert.Equal(365, dashboard.Activity.Weeks.SelectMany(x => x.Days).Count(x => x.InRange));
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
            review.LoadAsync().GetAwaiter().GetResult(); review.FlipCommand.Execute(null);
            var reviewView = new ReviewView { DataContext = review, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(reviewView, 959, 710); Render(reviewView, "review-v016.png");
            var allTopics = Descendants(reviewView).OfType<ToggleButton>().Single(x => x.Content as string == "全部單字庫");
            allTopics.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            review.CategoryScope.Choices[0].ToggleCommand.Execute(null);
            Assert.True(allTopics.IsChecked);
            Assert.All(Descendants(reviewView).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewExampleEnglish"), x => Assert.Equal(23, x.FontSize));
            AssertFits(reviewView, "RateGood"); AssertFits(reviewView, "ReviewMeaning");
            Layout(reviewView, 715, 560); AssertFits(reviewView, "RateGood"); Render(reviewView, "review-v016-minimum.png");
            var editor = new EditorViewModel(store, new FixedGenerator(), word, _ => { });
            editor.LoadAsync().GetAwaiter().GetResult();
            var editorView = new EditorView { DataContext = editor, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"], FontFamily = new("Segoe UI, Microsoft JhengHei UI") };
            Layout(editorView, 959, 710); Render(editorView, "editor-v016.png");
            var dictionary = Assert.Single(Descendants(editorView).OfType<DictionaryBrowser>());
            Assert.True(dictionary.ActualWidth > 330);
            Assert.Single(Descendants(editorView).OfType<GridSplitter>());
            AssertFits(editorView, "SaveWord");
        });
    }

    private sealed class UnusedBackup : IBackupService
    {
        public Task CreateBackupAsync(string destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreBackupAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class LearningFlowStorageTests
{
    [Fact]
    public async Task MultipleTopicsUseUnionWithoutDuplicatesAndPrioritizeDueWords()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        var both = StudyTestData.Word("both") with { Categories = ["旅行", "商業"] };
        var business = StudyTestData.Word("business") with { Categories = ["商業"] };
        var other = StudyTestData.Word("other") with { Categories = ["其他"] };
        await data.Store.SaveVocabularyBatchAsync([both, business, other]);
        string[] selected = ["旅行", "商業"];
        Assert.Equal(2, (await data.Store.GetDashboardAsync(data.Clock.Now, categories: selected)).NewCount);
        var first = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5, categories: selected))!;
        var result = await data.Store.SubmitReviewAsync(new(first.Word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), first.ScheduleVersion));
        var due = (await data.Store.GetNextReviewAsync(result.DueAt, 5, categories: selected))!;
        Assert.Equal(first.Word.Id, due.Word.Id); Assert.False(due.IsNew);
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5, categories: ["missing' OR 1=1 --"]));
        var library = new LibraryViewModel(data.Store, _ => { }); await library.LoadAsync();
        library.CategoryScope.SetSelected(selected); await library.SearchCommand.ExecuteAsync();
        Assert.Equal(2, library.Items.Count); Assert.DoesNotContain(library.Items, x => x.Id == other.Id);
        var chips = library.CategoryScope;
        Assert.False(chips.Choices[0].IsSelected);
        chips.Choices[0].ToggleCommand.Execute(null);
        while (library.IsBusy) await Task.Delay(5);
        Assert.Empty(chips.SelectedNames); Assert.Equal(3, library.Items.Count);
    }

    [Fact]
    public async Task ActivityUsesLocalDaysDeduplicatesRepeatedWordsAndExcludesUndo()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var first = await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Again, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        data.Clock.Now = first.DueAt;
        var next = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var second = await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), next.ScheduleVersion));
        var day = DateOnly.FromDateTime(data.Clock.Now.UtcDateTime);
        Assert.Equal(1, Assert.Single(await data.Store.GetStudyActivityAsync(day, day)).ReviewedCount);
        await data.Store.UndoReviewAsync(second.OperationId);
        Assert.Equal(1, Assert.Single(await data.Store.GetStudyActivityAsync(day, day)).ReviewedCount);
        using var fresh = new StudyTestData(); await fresh.Store.InitializeAsync();
        await fresh.AddWordAsync(); var only = (await fresh.Store.GetNextReviewAsync(fresh.Clock.Now, 5))!;
        var undone = await fresh.Store.SubmitReviewAsync(new(only.Word.Id, ReviewRating.Good, fresh.Clock.Now, Guid.NewGuid(), only.ScheduleVersion));
        await fresh.Store.UndoReviewAsync(undone.OperationId);
        Assert.Empty(await fresh.Store.GetStudyActivityAsync(day, day));
        var stats = LearningActivity.Build([new(day.AddDays(-1), 3), new(day.AddDays(-2), 4), new(day.AddDays(-4), 1)], day);
        Assert.Equal(8, stats.Total); Assert.Equal(3, stats.ActiveDays); Assert.Equal(2, stats.Streak);
        Assert.Equal(365, stats.Weeks.SelectMany(x => x.Days).Count(x => x.InRange));
    }
}
