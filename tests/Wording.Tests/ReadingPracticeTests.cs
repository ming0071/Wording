using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class ReadingPracticeTests
{
    [Theory]
    [InlineData("Please confirm your appointment.", "please confirm your appointment")]
    [InlineData("I'm ready, and we don't need 9 tickets.", "I am ready and we do not need nine tickets")]
    [InlineData("We can't wait.", "We cannot wait!")]
    public void ComparisonAcceptsCasePunctuationCommonContractionsAndSmallNumbers(string expected, string heard)
    {
        var comparison = ReadingComparison.Compare(expected, heard);
        Assert.True(comparison.IsMatch);
        Assert.Equal(0, comparison.Different + comparison.Missing + comparison.Extra);
    }

    [Fact]
    public void ComparisonReportsMissingExtraAndDifferentWordsWithoutGivingAPronunciationScore()
    {
        var missing = ReadingComparison.Compare("Please confirm your appointment tomorrow morning.", "Please confirm your appointment tomorrow.");
        Assert.False(missing.IsMatch); Assert.Equal(1, missing.Missing);
        Assert.Contains(missing.Words, x => x.Kind == ReadingDifferenceKind.Missing && x.Expected == "morning");
        var extra = ReadingComparison.Compare("Please confirm your appointment.", "Please confirm your new appointment.");
        Assert.Equal(1, extra.Extra);
        Assert.Contains(extra.Words, x => x.Kind == ReadingDifferenceKind.Extra && x.Heard == "new");
        var different = ReadingComparison.Compare("Please confirm your appointment.", "Please cancel your appointment.");
        Assert.Equal(1, different.Different);
        Assert.Contains(different.Words, x => x.Expected == "confirm" && x.Heard == "cancel");
        Assert.False(ReadingComparison.Compare("", "").IsMatch);
        Assert.Throws<ArgumentException>(() => ReadingComparison.Compare(string.Join(" ", Enumerable.Repeat("word", 501)), "word"));
    }

    [Fact]
    public async Task ReadingEntryPassesTheSelectedExampleWithoutChangingAnyReviewState()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, new());
        var vm = new ReviewViewModel(data.Store, new PracticeFixtures.Speech(), new() { AutoSpeakWord = false, AutoSpeakExamples = false }, reading);
        vm.BeginSession(); await vm.LoadAsync();
        reading.Ready();
        Assert.False(reading.RecordCommand.CanExecute(null));
        vm.FlipCommand.Execute(null);
        var before = (await data.Store.GetPracticeCandidatesAsync()).Select(x => x.Schedule).ToArray();
        var summary = await data.Store.GetDashboardAsync(data.Clock.Now);
        Assert.True(reading.RecordCommand.CanExecute(null)); reading.RecordCommand.Execute(null);
        reading.Recording(reading.Run); reading.RecordCommand.Execute(null);
        reading.Complete(reading.Run, vm.Current!.Word.Examples[0].English);
        Assert.True(vm.Definitions[0].Examples[0].HasResult);
        Assert.Equal(before, (await data.Store.GetPracticeCandidatesAsync()).Select(x => x.Schedule));
        Assert.Equal(summary, await data.Store.GetDashboardAsync(data.Clock.Now));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
    }

    [Fact]
    public void SwitchingSentenceAndCardInvalidatesLateResultsWithoutReconnecting()
    {
        var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, new());
        var first = new ReviewExampleViewModel(new("First sentence.", "第一句"), reading);
        var second = new ReviewExampleViewModel(new("Second sentence.", "第二句"), reading);
        var actions = new List<string>(); reading.Requested += (action, _) => actions.Add(action);
        reading.SetExamples([first, second]); reading.SetAnswerVisible(true); reading.Ready();
        reading.RecordCommand.Execute(null); var oldRun = reading.Run;
        reading.Recording(oldRun); reading.NextCommand.Execute(null);
        Assert.True(second.IsSelected); Assert.Equal(ReadingPhase.Finishing, reading.Phase);
        reading.Complete(oldRun, "First sentence."); Assert.False(second.HasResult);
        reading.Cancelled(reading.Run); reading.RecordCommand.Execute(null);
        reading.Recording(reading.Run); reading.RecordCommand.Execute(null);
        reading.Complete(reading.Run, "Second sentence."); Assert.True(second.HasResult);
        Assert.True(reading.ReplayCommand.CanExecute(null));
        reading.SetExamples([new(new("Third sentence.", "第三句"), reading)]);
        Assert.False(reading.ReplayCommand.CanExecute(null)); Assert.Equal(ReadingPhase.Ready, reading.Phase);
        Assert.DoesNotContain("warm", actions);
    }

    [Fact]
    public async Task SpaceFlipsThenRecordsAndCustomKeysRespectRepeatsAndEditing()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        var settings = new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false };
        var reading = new ReadingPracticeViewModel(new(), "unused", () => { }, settings);
        var review = new ReviewViewModel(data.Store, new PracticeFixtures.Speech(), settings, reading);
        review.BeginSession(); await review.LoadAsync(); reading.Ready();
        Assert.False(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.R, System.Windows.Input.ModifierKeys.Control, false));
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None, false));
        Assert.True(review.IsAnswerVisible);
        Assert.Equal(ReadingPhase.Ready, reading.Phase);
        OffscreenWpf.Invoke(() => Assert.False(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.Space,
            System.Windows.Input.ModifierKeys.None, false, new System.Windows.Controls.TextBox())));
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None, true));
        Assert.Equal(ReadingPhase.Ready, reading.Phase);
        reading.ConnectionInterrupted(true);
        Assert.False(reading.RecordCommand.CanExecute(null));
        Assert.Contains("15 秒", reading.Status);
        reading.ConnectionInterrupted(false);
        Assert.True(reading.RecordCommand.CanExecute(null));
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None, false));
        Assert.Equal(ReadingPhase.Starting, reading.Phase);
        reading.Recording(reading.Run);
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None, false));
        Assert.Equal(ReadingPhase.Finishing, reading.Phase);
        reading.Cancelled(reading.Run);
        settings.RecordReadingKey = "F7"; settings.ReplayReadingKey = "F8";
        Assert.Same(reading.RecordCommand, review.CommandForShortcut("F7"));
        Assert.Same(reading.ReplayCommand, review.CommandForShortcut("F8"));
        Assert.NotSame(reading.RecordCommand, review.CommandForShortcut("Space"));
        Assert.Contains("F7", reading.RecordLabel); Assert.Contains("F8", reading.ReplayLabel);
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.F7, System.Windows.Input.ModifierKeys.None, false));
        reading.Recording(reading.Run);
        Assert.Contains("F7", reading.Status);
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.F7, System.Windows.Input.ModifierKeys.None, false));
        Assert.Equal(ReadingPhase.Finishing, reading.Phase);
        reading.Complete(reading.Run, review.Current!.Word.Examples[0].English);
        var replayed = false; reading.Requested += (action, _) => replayed |= action == "replay";
        Assert.True(Wording.Desktop.ReviewKeyboard.Handle(review, System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.None, false));
        Assert.True(replayed);
    }
}
