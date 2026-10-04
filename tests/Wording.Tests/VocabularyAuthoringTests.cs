using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.Views;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task SpeechShortcutsReadWordAndAllExamplesWithCustomKeys()
    {
        var word = StudyTestData.Word() with { Examples = [new("First example.", "第一句"), new("Second example.", "第二句")] };
        var store = new RecordingStore { NextReview = new(word, 0, true, null) };
        var speech = new RecordingSpeech();
        var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
        var review = new ReviewViewModel(store, speech, settings);
        await review.LoadAsync();
        Assert.True(ReviewKeyboard.Handle(review, Key.S, ModifierKeys.None, false));
        Assert.Equal(word.Headword, Assert.Single(speech.Spoken));
        ReviewKeyboard.Handle(review, Key.S, ModifierKeys.None, true);
        ReviewKeyboard.Handle(review, Key.E, ModifierKeys.None, false);
        Assert.Single(speech.Spoken);
        ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false);
        ReviewKeyboard.Handle(review, Key.E, ModifierKeys.None, false);
        Assert.Equal("First example. Second example.", speech.Spoken[1]);
        settings.SpeakKey = "F7";
        settings.SpeakExampleKey = "F8";
        Assert.False(ReviewKeyboard.Handle(review, Key.S, ModifierKeys.None, false));
        ReviewKeyboard.Handle(review, Key.F7, ModifierKeys.None, false);
        ReviewKeyboard.Handle(review, Key.F8, ModifierKeys.None, false);
        Assert.Equal(new[] { word.Headword, "First example. Second example.", word.Headword, "First example. Second example." }, speech.Spoken);
        Assert.Contains("F7", review.SpeakButtonText);
    }

    [Fact]
    public async Task MultipleSenseEditorSavesEachMeaningAndManualExtrasTogether()
    {
        var store = new RecordingStore();
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { })
        { Headword = "bank", PartOfSpeech = "名詞", Meaning = "銀行", SynonymsText = "lender、LENDER, financial institution", Notes = "存錢的地方" };
        editor.AddSenseCommand.Execute(null);
        var river = Assert.Single(editor.AdditionalSenses);
        river.Meaning = "河岸";
        river.Cue = "beside a river";
        river.SynonymsText = "shore";
        river.Notes = "river bank";
        river.English = "We sat on the river bank.";
        river.Chinese = "我們坐在河岸。";
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal("", editor.Error);
        Assert.False(editor.IsDirty);
        var word = store.Saved!;
        var definition = Assert.Single(word.AdditionalSenses);
        Assert.NotEqual(word.Id, definition.Id);
        Assert.Equal(new[] { "lender", "financial institution" }, word.Synonyms);
        Assert.Equal("存錢的地方", word.Notes);
        Assert.Equal("河岸", definition.Meaning);
        Assert.Equal("shore", Assert.Single(definition.Synonyms));
        Assert.Equal("river bank", definition.Notes);
        Assert.Equal("We sat on the river bank.", Assert.Single(definition.Examples).English);
    }

    [Fact]
    public async Task IncompleteAdditionalSenseBlocksSavingAndRemovalMakesDraftValid()
    {
        var store = new RecordingStore();
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { })
        { Headword = "bank", PartOfSpeech = "名詞", Meaning = "銀行" };
        editor.AddSenseCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync();
        Assert.Contains("每個詞義", editor.Error);
        Assert.Null(store.Saved);
        var sense = Assert.Single(editor.AdditionalSenses);
        sense.Meaning = "河岸";
        sense.English = "River bank.";
        await editor.SaveCommand.ExecuteAsync();
        Assert.Contains("繁中翻譯", editor.Error);
        Assert.Null(store.Saved);
        editor.RemoveSenseCommand.Execute(sense);
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal("銀行", store.Saved!.Meaning);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task AdditionalSenseEditsDuringSaveRemainDirtyWithoutDuplicateCardsOnRetry()
    {
        var release = new TaskCompletionSource<bool>();
        var store = new RecordingStore { SaveCompletion = release.Task };
        var navigations = 0;
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => navigations++)
        { Headword = "bank", PartOfSpeech = "名詞", Meaning = "銀行" };
        editor.AddSenseCommand.Execute(null);
        var sense = Assert.Single(editor.AdditionalSenses);
        sense.Meaning = "河岸";
        var saving = editor.SaveCommand.ExecuteAsync();
        Assert.False(editor.AddSenseCommand.CanExecute(null));
        Assert.False(editor.RemoveSenseCommand.CanExecute(sense));
        sense.Notes = "新的筆記";
        release.SetResult(true);
        await saving;
        Assert.True(editor.AddSenseCommand.CanExecute(null));
        Assert.True(editor.RemoveSenseCommand.CanExecute(sense));
        Assert.True(editor.IsDirty);
        Assert.Equal(0, navigations);
        Assert.Equal("", store.Saved!.AdditionalSenses[0].Notes);
        var savedId = store.Saved.AdditionalSenses[0].Id;
        await editor.SaveCommand.ExecuteAsync();
        Assert.False(editor.IsDirty);
        Assert.Equal(1, navigations);
        Assert.Equal(savedId, store.Saved.AdditionalSenses[0].Id);
        Assert.Equal("新的筆記", store.Saved.AdditionalSenses[0].Notes);
    }

    [Fact]
    public void VerticalReviewShowsExtrasWithoutTooltipsAndEditorUsesPartOfSpeechChips()
    {
        OffscreenWpf.Invoke(() =>
        {
            var word = StudyTestData.Word("bank", "銀行") with
            {
                Cue = "a place to keep and borrow money", Collocations = ["open a bank account"],
                Examples = [new("I opened a bank account yesterday.", "我昨天開了一個銀行帳戶。")],
                Synonyms = ["financial institution"], Notes = "和河岸的意思不同。"
            };
            var store = new RecordingStore { NextReview = new(word, 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
            review.LoadAsync().GetAwaiter().GetResult();
            review.FlipCommand.Execute(null);
            var view = new ReviewView { DataContext = review, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            Layout(view, 715, 560);
            foreach (var id in new[] { "ReviewHeadword", "ReviewMeaning", "ReviewSynonyms", "ReviewNotes", "RateGood", "SpeakWord", "SpeakExamples" }) AssertFits(view, id);
            var visible = Descendants(view).OfType<TextBlock>().Where(IsShown).ToArray();
            Assert.Contains(visible, x => x.Text == "financial institution");
            Assert.Contains(visible, x => x.Text == word.Notes);
            var head = visible.Single(x => x.Text == word.Headword).TransformToAncestor(view).Transform(new Point());
            var meaning = visible.Single(x => x.Text == word.Meaning).TransformToAncestor(view).Transform(new Point());
            var synonyms = visible.Single(x => x.Text == "financial institution").TransformToAncestor(view).Transform(new Point());
            Assert.True(head.Y < meaning.Y && meaning.Y < synonyms.Y);
            Assert.All(Descendants(view).OfType<FrameworkElement>(), x => Assert.Null(x.ToolTip));
            Render(view, "review-v014-notes.png");
            var editor = new EditorViewModel(store, new FixedGenerator(), word, _ => { });
            var editorView = new EditorView { DataContext = editor, FontSize = 14 };
            Layout(editorView, 959, 710);
            var picker = Assert.Single(Descendants(editorView).OfType<ListBox>(), x => AutomationProperties.GetAutomationId(x) == "PartOfSpeechInput");
            Assert.Equal("noun", picker.SelectedValue);
            Assert.Contains("動詞", editor.PartOfSpeechOptions);
            picker.SelectedValue = "動詞";
            Assert.Equal("動詞", editor.PartOfSpeech);
            Assert.True(editor.IsDirty);
            editor.AddSenseCommand.Execute(null);
            Layout(editorView, 959, 710);
            Assert.Single(editor.AdditionalSenses);
            Assert.Equal(2, Descendants(editorView).OfType<ListBox>().Count(x => AutomationProperties.GetAutomationId(x) == "PartOfSpeechInput"));
        });
    }

    private sealed class RecordingSpeech : IPronunciationService
    {
        public List<string> Spoken { get; } = [];
        public string Status => "test";
        public void Speak(string text) => Spoken.Add(text);
        public void Stop() { }
        public void Dispose() { }
    }
}

public sealed class VocabularyStorageTests
{
    [Fact]
    public async Task MultipleSensesAndExtrasSurviveRestartBackupAndKeepExistingProgress()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var bank = StudyTestData.Word("bank", "銀行");
        await data.Store.SaveVocabularyAsync(bank);
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var result = await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        var river = StudyTestData.Word("bank", "河岸") with { Cue = "beside a river", Synonyms = ["shore"], Notes = "河邊" };
        bank = bank with { Synonyms = ["lender"], Notes = "存錢" };
        await data.Store.SaveVocabularyBatchAsync([bank, river]);
        Assert.Equal(2, (await data.NewStore().GetVocabularyAsync()).Count);
        var next = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, 5))!;
        Assert.Equal(river.Id, next.Word.Id);
        Assert.True(next.IsNew);
        var archive = Path.Combine(data.DirectoryPath, "extras.zip");
        var backup = new BackupService(data.Store);
        await backup.CreateBackupAsync(archive);
        await data.Store.SaveVocabularyAsync(bank with { Notes = "別的內容" });
        await backup.RestoreBackupAsync(archive);
        var actual = (await data.NewStore().GetVocabularyAsync()).ToDictionary(x => x.Id);
        Assert.Equal("存錢", actual[bank.Id].Notes);
        Assert.Equal("shore", Assert.Single(actual[river.Id].Synonyms));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        var due = (await data.Store.GetNextReviewAsync(result.DueAt, 0))!;
        Assert.Equal(bank.Id, due.Word.Id);
        Assert.False(due.IsNew);
    }

    [Fact]
    public async Task MultipleSenseSaveRollsBackAllChangesOnFailure()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var original = await data.AddWordAsync();
        data.FailAt = "Vocabulary.AfterItem";
        await Assert.ThrowsAsync<IOException>(() => data.Store.SaveVocabularyBatchAsync(
            [original with { Notes = "修改" }, StudyTestData.Word("bank", "河岸")]));
        Assert.Equal("", Assert.Single(await data.Store.GetVocabularyAsync()).Notes);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        data.FailAt = null;
        await Assert.ThrowsAsync<ArgumentException>(() => data.Store.SaveVocabularyBatchAsync(
            [original with { Notes = "修改" }, StudyTestData.Word() with { Meaning = "" }]));
        Assert.Equal("", Assert.Single(await data.Store.GetVocabularyAsync()).Notes);
    }
}
