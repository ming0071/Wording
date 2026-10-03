using System.Text.Json;
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
    [Fact]
    public async Task WordUpSaveNextOnlyAdvancesAfterTheEntireDraftIsSaved()
    {
        var store = new RecordingStore();
        var advances = 0;
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { }, next: () => advances++)
            { Headword = "bank", PartOfSpeech = "名詞" };
        await editor.SaveNextCommand.ExecuteAsync();
        Assert.Equal(0, advances);
        editor.Meaning = "銀行";
        editor.AddSenseCommand.Execute(null);
        editor.AdditionalSenses[0].Meaning = "河岸";
        await editor.SaveNextCommand.ExecuteAsync();
        Assert.Equal("", editor.Error);
        Assert.Equal(1, advances);
        Assert.Equal(2, store.SavedBatch.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task AutoPronunciationPlaysOnLoadAndNextCardAndCanBeDisabled()
    {
        var first = StudyTestData.Word("bank");
        var second = StudyTestData.Word("invoice");
        var store = new RecordingStore { NextReview = new(first, 0, true, null) };
        store.Submit = submission =>
        {
            store.NextReview = new(second, 0, true, null);
            return Task.FromResult(new ReviewResult(submission.OperationId, DateTimeOffset.UtcNow.AddDays(1), "Review"));
        };
        var speech = new RecordingSpeech();
        var settings = new AppSettings { AutoSpeakExamples = false };
        var review = new ReviewViewModel(store, speech, settings);
        await review.LoadAsync();
        Assert.Equal(new[] { "bank" }, speech.Spoken);
        review.FlipCommand.Execute(null);
        Assert.Single(speech.Spoken);
        await review.GoodCommand.ExecuteAsync();
        Assert.Equal(new[] { "bank", "invoice" }, speech.Spoken);
        settings.AutoSpeakWord = false;
        await review.LoadAsync();
        Assert.Equal(2, speech.Spoken.Count);
        settings.AutoSpeakWord = true;
        store.NextReview = null;
        await review.LoadAsync();
        Assert.Equal(2, speech.Spoken.Count);
    }

    [Fact]
    public async Task WordUpEditorSavesEnglishDefinitionsAndMultipleExamplesAndTargetsAi()
    {
        var store = new RecordingStore();
        var generator = new FixedGenerator();
        var editor = new EditorViewModel(store, generator, null, _ => { })
        { Headword = "bank", PartOfSpeech = "名詞", Meaning = "銀行", EnglishDefinition = "A place to keep money.", AiConsent = true };
        editor.AddSenseCommand.Execute(null);
        var river = Assert.Single(editor.AdditionalSenses);
        river.Meaning = "河岸";
        river.EnglishDefinition = "Land beside a river.";
        river.Notes = "保留這段筆記";
        await editor.GenerateCommand.ExecuteAsync();
        Assert.Equal(river.Id, generator.Request!.Id);
        Assert.Equal("河岸", generator.Request.Meaning);
        editor.ApplyPreviewCommand.Execute(null);
        Assert.Equal("銀行", editor.Meaning);
        Assert.Equal("發票", river.Meaning);
        Assert.Equal("保留這段筆記", river.Notes);
        river.EnglishDefinition = "Land beside a river.";
        Assert.Equal(2, river.Examples.Count);
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal("", editor.Error);
        Assert.Equal("A place to keep money.", store.SavedBatch[0].EnglishDefinition);
        Assert.Equal("Land beside a river.", store.SavedBatch[1].EnglishDefinition);
        Assert.Equal(2, store.SavedBatch[1].Examples.Length);
    }

    [Fact]
    public void WordUpEditorRendersTextGroupsAndFixedSaveBar()
    {
        OffscreenWpf.Invoke(() =>
        {
            var editor = new EditorViewModel(new RecordingStore(), new FixedGenerator(), null, _ => { }, next: () => { })
            {
                Headword = "bank", PartOfSpeech = "名詞", Meaning = "銀行；金融機構",
                EnglishDefinition = "An organization where people keep and borrow money.",
                SynonymsText = "financial institution", Notes = "和 river bank（河岸）是不同的意思。"
            };
            editor.Examples[0].English = "I opened a bank account yesterday.";
            editor.Examples[0].Chinese = "我昨天開了一個銀行帳戶。";
            var view = new EditorView { DataContext = editor, FontSize = 14,
                FontFamily = new("Segoe UI, Microsoft JhengHei UI"), Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            Layout(view, 959, 710);
            foreach (var id in new[] { "HeadwordInput", "SaveWord", "SaveNextWord", "SearchDictionary", "PartOfSpeechInput" }) AssertFits(view, id);
            var meaning = Descendants(view).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "中文解釋");
            var definition = Descendants(view).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "英文解釋");
            Assert.True(meaning.TranslatePoint(new Point(), view).Y < definition.TranslatePoint(new Point(), view).Y);
            Render(view, "editor-v015-normal.png");
            Layout(view, 715, 560);
            foreach (var id in new[] { "HeadwordInput", "SaveWord", "SaveNextWord", "SearchDictionary" }) AssertFits(view, id);
            Render(view, "editor-v015-minimum.png");
            editor.AddSenseCommand.Execute(null);
            Layout(view, 959, 710);
            Assert.Equal(2, Descendants(view).OfType<ListBox>().Count(x => AutomationProperties.GetAutomationId(x) == "PartOfSpeechInput"));
        });
    }
}

public sealed class VocabularyFilesTests
{
    [Fact]
    public async Task JsonRoundTripRetainsProgressAndFlagsAndRepeatedNewImportsAreIdempotent()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        await data.Store.SetPausedAsync(word.Id, true);
        var scheduleBefore = data.Scalar("SELECT due_ms FROM cards;");
        var service = new VocabularyFileService(data.Store);
        var path = Path.Combine(data.DirectoryPath, "vocabulary.json");
        await service.ExportAsync(path);
        var document = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path), VocabularyFileService.JsonOptions)!;
        document = document with { Items = [document.Items[0] with { EnglishDefinition = "A meeting arranged in advance.", Notes = "AI 新增" },
            new() { Headword = "bank", PartOfSpeech = "名詞", Meaning = "河岸" }] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, VocabularyFileService.JsonOptions));
        Assert.Equal(2, await service.ImportAsync(path));
        Assert.Equal(2, await service.ImportAsync(path));
        var all = await data.Store.GetVocabularyAsync();
        Assert.Equal(2, all.Count);
        var updated = all.Single(x => x.Id == word.Id);
        Assert.True(updated.IsPaused);
        Assert.Equal("AI 新增", updated.Notes);
        Assert.Equal("A meeting arranged in advance.", updated.EnglishDefinition);
        Assert.Equal(scheduleBefore, data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{word.Id:D}';"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        document = document with { Items = [document.Items[0] with { Notes = "不應寫入" }, document.Items[1] with { EnglishDefinition = new string('a', 4001) }] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, VocabularyFileService.JsonOptions));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(path));
        Assert.Equal("AI 新增", (await data.Store.GetVocabularyAsync()).Single(x => x.Id == word.Id).Notes);
    }

    [Fact]
    public void CodexExecutableSelectsNewestInstalledVersionAndRejectsMissingExplicitPath()
    {
        using var data = new StudyTestData();
        var older = Path.Combine(data.DirectoryPath, "older", "codex.exe");
        var newer = Path.Combine(data.DirectoryPath, "newer", "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(older)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newer)!);
        File.WriteAllText(older, ""); File.WriteAllText(newer, "");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(newer, CodexExecutableLocator.FindDesktopExecutable(data.DirectoryPath));
        Assert.Equal(older, CodexExecutableLocator.Resolve(older));
        Assert.Throws<FileNotFoundException>(() => CodexExecutableLocator.Resolve(Path.Combine(data.DirectoryPath, "missing.exe")));
    }
}
