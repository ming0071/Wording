using System.Text.Json;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task DuplicateWordsReallyMergeIntoOneNewCardWithBackupAndOneStar()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var first = StudyTestData.Word("fill in", "代班");
        var second = StudyTestData.Word("fill in", "填表") with { IsStarred = true, Notes = "填表筆記" };
        var other = StudyTestData.Word("invoice", "發票");
        await data.Store.SaveVocabularyBatchAsync([first, second, other]);
        foreach (var word in new[] { first, second, other })
            await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        var otherDue = data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{other.Id:D}';");
        Assert.Equal(1, await data.Store.MergeDuplicateWordsAsync());
        var merged = (await data.Store.GetVocabularyAsync()).Single(x => x.Headword == "fill in");
        Assert.Equal(first.Id, merged.Id);
        Assert.True(merged.IsStarred);
        Assert.Equal("填表筆記", Assert.Single(merged.AdditionalSenses).Notes);
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM senses;"));
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal(1L, data.Scalar($"SELECT state FROM cards WHERE sense_id='{first.Id:D}';"));
        Assert.Equal(DBNull.Value, data.Scalar($"SELECT last_review_ms FROM cards WHERE sense_id='{first.Id:D}';"));
        Assert.Equal(otherDue, data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{other.Id:D}';"));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(data.DirectoryPath, "backups"), "*.wtbackup"));
        Assert.Equal(0, await data.Store.MergeDuplicateWordsAsync());
        Assert.Single(Directory.GetFiles(Path.Combine(data.DirectoryPath, "backups"), "*.wtbackup"));
        using var restored = new StudyTestData();
        await restored.Store.InitializeAsync();
        await new BackupService(restored.Store).RestoreBackupAsync(backup);
        Assert.Equal(3, (await restored.Store.GetVocabularyAsync()).Count);
        Assert.Equal(3L, restored.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task MergeFailureRollsBackContentCardsAndLearningRecords()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var first = StudyTestData.Word("check out", "退房");
        var second = StudyTestData.Word("check out", "查看");
        await data.Store.SaveVocabularyBatchAsync([first, second]);
        await data.Store.SubmitReviewAsync(new(second.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        data.FailAt = "WordMerge.AfterGroup";
        await Assert.ThrowsAsync<IOException>(() => data.Store.MergeDuplicateWordsAsync());
        Assert.Equal(2, (await data.Store.GetVocabularyAsync()).Count);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        data.FailAt = null;
        Assert.Equal(1, await data.Store.MergeDuplicateWordsAsync());
    }

    [Fact]
    public async Task LegacyJsonReimportAndEditingKeepOneWordAndItsNestedDefinitions()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var legacy = new VocabularyDocument(1,
        [
            new() { Id = Guid.NewGuid(), Headword = "fill in", PartOfSpeech = "動詞片語", Meaning = "代班", Categories = ["職場"] },
            new() { Id = Guid.NewGuid(), Headword = "fill in", PartOfSpeech = "動詞片語", Meaning = "填表", Categories = ["申請"] }
        ]);
        var path = Path.Combine(data.DirectoryPath, "legacy.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy, VocabularyFileService.JsonOptions));
        var service = new VocabularyFileService(data.Store);
        Assert.Equal(1, await service.ImportAsync(path));
        var word = Assert.Single(await data.Store.GetVocabularyAsync());
        await data.Store.SetStarredAsync(word.Id, true);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        Assert.Equal(1, await service.ImportAsync(path));
        word = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.True(word.IsStarred);
        Assert.Equal(2, word.Definitions.Count);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        var editor = new EditorViewModel(data.Store, new FixedGenerator(), word, _ => { });
        await editor.LoadAsync();
        Assert.False(editor.IsDirty);
        Assert.Single(editor.AdditionalSenses).Notes = "保留第二組的筆記";
        await editor.SaveCommand.ExecuteAsync();
        Assert.Empty(editor.Error);
        word = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal("保留第二組的筆記", Assert.Single(word.AdditionalSenses).Notes);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        var edited = word with { AdditionalSenses = [word.AdditionalSenses[0] with { Meaning = "填寫表格" }] };
        await data.Store.SaveVocabularyAsync(edited);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Single(await data.Store.GetVocabularyAsync("填寫表格"));
        var library = new LibraryViewModel(data.Store, _ => { }) { SearchText = "填寫表格" };
        await library.LoadAsync();
        Assert.Single(library.Items);
        Assert.Contains("1 個單字／片語 · 2 組解釋", library.CountText);
    }

    [Fact]
    public async Task NewEditorEntryForExistingHeadwordAddsMeaningWithoutAnotherStarOrCard()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("fill in", "代班") with { IsStarred = true };
        await data.Store.SaveVocabularyAsync(word);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        var draft = StudyTestData.Word("FILL IN", "填表");
        var editor = new EditorViewModel(data.Store, new FixedGenerator(), draft, _ => { });
        await editor.LoadAsync();
        await editor.SaveCommand.ExecuteAsync();
        Assert.Empty(editor.Error);
        var combined = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal(word.Id, combined.Id);
        Assert.True(combined.IsStarred);
        Assert.Equal("填表", Assert.Single(combined.AdditionalSenses).Meaning);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task AddingAnotherMeaningResetsOnlyThatWordAndRepeatedImportKeepsItsNewProgress()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("fill in", "代班");
        await data.Store.SaveVocabularyAsync(word);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        var path = Path.Combine(data.DirectoryPath, "new-meaning.json");
        var document = new VocabularyDocument(1, [new()
        {
            Id = Guid.NewGuid(), Headword = "fill in", PartOfSpeech = "noun", Meaning = "填表"
        }]);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, VocabularyFileService.JsonOptions));
        var service = new VocabularyFileService(data.Store);
        Assert.Equal(1, await service.ImportAsync(path));
        var combined = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal(word.Id, combined.Id);
        Assert.Equal("代班", combined.Meaning);
        Assert.Equal("填表", Assert.Single(combined.AdditionalSenses).Meaning);
        Assert.Equal(DBNull.Value, data.Scalar($"SELECT last_review_ms FROM cards WHERE sense_id='{word.Id:D}';"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 25))!;
        Assert.True(card.IsNew);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.Equal(1, await service.ImportAsync(path));
        Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task CombinedLibraryAndReviewShowOneStarAndAllMeanings()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("check out", "辦理退房") with
        {
            AdditionalSenses = [new() { PartOfSpeech = "動詞片語", Meaning = "查看；檢查", Examples = [new("Check it out.", "查看一下。")] }]
        };
        await data.Store.SaveVocabularyAsync(word);
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var library = new LibraryViewModel(data.Store, _ => { });
            await library.LoadAsync();
            var view = new LibraryView { DataContext = library };
            Layout(view, 959, 710);
            Assert.Single(Descendants(view).OfType<System.Windows.Controls.Button>(), x => x.Tag is bool);
            Render(view, "library-word-merged.png");
            var review = new ReviewViewModel(data.Store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false });
            await review.LoadAsync();
            Assert.Contains("一起複習", review.SenseContext);
            review.FlipCommand.Execute(null);
            var card = new ReviewView { DataContext = review };
            Layout(card, 959, 710);
            Render(card, "review-word-merged.png");
            Assert.Contains(Descendants(card).OfType<System.Windows.Controls.TextBlock>(), x => x.Text.Contains("查看；檢查") ||
                x.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text.Contains("查看；檢查")));
            Assert.Single(Descendants(card).OfType<System.Windows.Controls.Button>(), x => x.Tag is bool);
        });
    }

    [Theory]
    [InlineData("toeic-vocabulary.json")]
    [InlineData("toeic-phrasal-verbs.json")]
    [InlineData("toeic-starter.json")]
    [InlineData("vocabulary-example.json")]
    public async Task PacksContainOneItemPerHeadwordWithNestedMeanings(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "content", fileName);
        var document = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path), VocabularyFileService.JsonOptions)!;
        Assert.Equal(document.Items.Length, document.Items.Select(x => WordIdentity.For(x.Headword)).Distinct().Count());
        Assert.All(document.Items, entry => Assert.Equal(WordIdentity.For(entry.Headword), entry.WordId));
        Assert.All(document.Items.SelectMany(entry => new[] { entry.Notes }.Concat(entry.AdditionalSenses.Select(x => x.Notes))),
            notes => Assert.DoesNotContain(VocabularyNotes.PackDisclaimer, notes));
        Assert.Equal(WordIdentity.For("fill in"), WordIdentity.For("  FILL   IN  "));
    }
}
