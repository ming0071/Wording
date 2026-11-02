using System.Windows.Input;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class ReviewReadingShortcutsTests
{
    [Fact]
    public async Task NextShortcutWrapsAcrossMeaningsAndIgnoresHeldKeysAndTextEditing()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        await data.Store.SaveVocabularyAsync(StudyTestData.Word() with {
            Examples = [new("First sentence.", "一"), new("Second sentence.", "二")],
            AdditionalSenses = [new() { Meaning = "其他意思", Examples = [new("Third sentence.", "三")] }] });
        var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
        var reading = new ReadingPracticeViewModel(new(), data.DirectoryPath, () => { }, settings);
        var review = new ReviewViewModel(data.Store, new PracticeFixtures.Speech(), settings, reading);
        await review.LoadAsync(); Assert.False(ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, false));
        review.FlipCommand.Execute(null); var examples = review.Definitions.SelectMany(x => x.Examples).ToArray();
        Assert.True(examples[0].IsSelected);
        Assert.True(ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, false)); Assert.True(examples[1].IsSelected);
        ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, true); Assert.True(examples[1].IsSelected);
        OffscreenWpf.Invoke(() => Assert.False(ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, false, new System.Windows.Controls.TextBox())));
        ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, false); Assert.True(examples[2].IsSelected);
        ReviewKeyboard.Handle(review, Key.N, ModifierKeys.None, false); Assert.True(examples[0].IsSelected);
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public void OldCustomNIsPreservedWhenNextSentenceShortcutIsAdded()
    {
        using var data = new StudyTestData();
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"SpeakKey\":\"N\"}");
        var settings = AppSettings.Load(data.DirectoryPath);
        Assert.Equal("N", settings.SpeakKey); Assert.NotEqual("N", settings.NextReadingKey); settings.Validate();
        settings.NextReadingKey = "F9"; settings.Save(data.DirectoryPath);
        Assert.Equal("F9", AppSettings.Load(data.DirectoryPath).NextReadingKey);
    }
}
