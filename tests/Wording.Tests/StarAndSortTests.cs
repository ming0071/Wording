using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;
using Wording.Desktop.Views;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task StarsPersistWithoutChangingScheduleAndSurviveStaleContentSave()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var original = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.NotNull(original.CreatedAt);
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SetStarredAsync(word.Id, true);
        await data.Store.SaveVocabularyAsync(original with { Notes = "Updated" });
        var saved = Assert.Single(await data.NewStore().GetVocabularyAsync());
        Assert.True(saved.IsStarred);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(card.ScheduleVersion, (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!.ScheduleVersion);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.True(Assert.Single(await data.Store.GetVocabularyAsync()).Stability > 0);
        await data.Store.SetStarredAsync(word.Id, false);
        Assert.False(Assert.Single(await data.NewStore().GetVocabularyAsync()).IsStarred);
    }

    [Fact]
    public async Task LibraryAndReviewStarsUpdateWithoutLosingSelectionOrAnswer()
    {
        var store = new RecordingStore();
        var word = StudyTestData.Word();
        await store.SaveVocabularyAsync(word);
        var library = new LibraryViewModel(store, _ => { });
        await library.LoadAsync();
        await library.StarCommand.ExecuteAsync();
        Assert.True(library.SelectedItem!.IsStarred);
        store.NextReview = new(library.SelectedItem, 7, false, null);
        var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
        await review.LoadAsync();
        review.FlipCommand.Execute(null);
        await review.StarCommand.ExecuteAsync();
        Assert.False(review.Current!.Word.IsStarred);
        Assert.True(review.IsAnswerVisible);
        Assert.Equal(7, review.Current.ScheduleVersion);
    }

    [Fact]
    public void LibrarySortsByCreatedAlphabetStabilityAndStars()
    {
        var a = StudyTestData.Word("apple") with { Stability = 5, CreatedAt = DateTimeOffset.UtcNow };
        var b = StudyTestData.Word("Banana") with { Stability = 0, IsStarred = true };
        var c = StudyTestData.Word("cherry") with { Stability = 20 };
        VocabularyItem[] items = [b, c, a];
        Assert.Equal(new[] { a, b, c }, LibraryViewModel.SortItems(items, "單字：A–Z"));
        Assert.Equal(new[] { c, b, a }, LibraryViewModel.SortItems(items, "單字：Z–A"));
        Assert.Equal(new[] { b, a, c }, LibraryViewModel.SortItems(items, "熟練程度：低到高"));
        Assert.Equal(new[] { c, a, b }, LibraryViewModel.SortItems(items, "熟練程度：高到低"));
        Assert.Equal(new[] { b, a, c }, LibraryViewModel.SortItems(items, "星號優先"));
        Assert.Equal(new[] { a, c, b }, LibraryViewModel.SortItems(items, "建立時間：最新優先"));
        Assert.Equal(new[] { b, c, a }, LibraryViewModel.SortItems(items, "建立時間：最早優先"));
        var oldFirst = a with { CreatedAt = null, CreationOrder = 1 };
        var oldLast = b with { CreatedAt = null, CreationOrder = 20 };
        Assert.Equal(new[] { oldLast, oldFirst }, LibraryViewModel.SortItems([oldFirst, oldLast], "建立時間：最新優先"));
    }

    [Fact]
    public void StarAndSortLayoutFitsLibraryAndReview()
    {
        OffscreenWpf.Invoke(() =>
        {
            var store = new RecordingStore();
            store.SaveVocabularyAsync(StudyTestData.Word("appointment") with { IsStarred = true }).GetAwaiter().GetResult();
            var library = new LibraryViewModel(store, _ => { });
            library.LoadAsync().GetAwaiter().GetResult();
            var view = new LibraryView { DataContext = library };
            Layout(view, 959, 710);
            var star = Descendants(view).OfType<System.Windows.Controls.Button>().Single(x => x.Tag is bool);
            Assert.Equal("取消星號", System.Windows.Automation.AutomationProperties.GetName(star));
            Assert.Equal(System.Windows.Media.Colors.White, ((System.Windows.Media.SolidColorBrush)((System.Windows.Shapes.Path)star.Template.FindName("Star", star)).Fill).Color);
            Render(view, "library-stars-v019.png");
            store.NextReview = new(store.Saved!, 0, true, null);
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
            review.LoadAsync().GetAwaiter().GetResult();
            var card = new ReviewView { DataContext = review };
            Layout(card, 959, 710);
            Render(card, "review-stars-v019.png");
            review.StarCommand.ExecuteAsync().GetAwaiter().GetResult();
            Layout(card, 959, 710);
            var unstarred = Descendants(card).OfType<System.Windows.Controls.Button>().Single(x => x.Tag is bool);
            Assert.Equal("標為不熟悉", System.Windows.Automation.AutomationProperties.GetName(unstarred));
            Assert.Equal(System.Windows.Media.Colors.White, ((System.Windows.Media.SolidColorBrush)((System.Windows.Shapes.Path)unstarred.Template.FindName("Star", unstarred)).Fill).Color);
            Assert.NotEqual(((System.Windows.Media.SolidColorBrush)star.Background).Color,
                ((System.Windows.Media.SolidColorBrush)unstarred.Background).Color);
            Render(card, "review-star-unselected.png");
        });
    }
}
