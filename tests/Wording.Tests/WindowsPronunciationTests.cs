using System.Windows.Input;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class WindowsPronunciationTests
{
    [Fact]
    public async Task PreviousAiSettingUsesLocalSpeechForReviewExamplesAndListening()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        File.WriteAllText(Path.Combine(data.DirectoryPath, "settings.json"), "{\"PronunciationProvider\":\"Ai\"}");
        var settings = AppSettings.Load(data.DirectoryPath);
        var windows = new PracticeFixtures.Speech();
        var main = new MainViewModel(data.Store, new BackupService(data.Store), new PracticeFixtures.Generator(),
            windows, settings, new(), data.DirectoryPath);
        var review = main.Review;
        review.BeginSession();
        await review.LoadAsync();
        Assert.Equal(review.Current!.Word.Headword, windows.Spoken);
        Assert.True(review.IsSpeaking);
        Assert.True(ReviewKeyboard.Handle(review, Key.Escape, ModifierKeys.None, false));
        Assert.Equal(PlaybackState.Stopped, windows.Playback);
        review.FlipCommand.Execute(null);
        var examples = string.Join(" ", review.Definitions.SelectMany(x => x.Examples).Select(x => x.English));
        Assert.Equal(examples, windows.Spoken);
        review.SpeakCommand.Execute(null);
        Assert.Equal(review.Current.Word.Headword, windows.Spoken);
        review.SpeakExampleCommand.Execute(null);
        Assert.Equal(examples, windows.Spoken);
        review.Reading!.Connecting();
        var example = review.Definitions[0].Examples[0];
        Assert.True(review.Reading.PronounceCommand.CanExecute(example.English));
        review.Reading.PronounceCommand.Execute(example.English);
        Assert.Equal(example.English, windows.Spoken);
        review.EndSession();
        Assert.Equal(PlaybackState.Stopped, windows.Playback);

        var practice = main.Practice!;
        practice.ListeningCommand.Execute(null);
        await practice.LoadAsync();
        await practice.GenerateCommand.ExecuteAsync();
        Assert.Empty(practice.Error);
        practice.PlayCommand.Execute(null);
        Assert.StartsWith("Please confirm", windows.Spoken);
        Assert.Equal(PlaybackState.Playing, windows.Playback);
        practice.PauseCommand.Execute(null);
        Assert.Equal(PlaybackState.Paused, windows.Playback);
        practice.ResumeCommand.Execute(null);
        Assert.Equal(PlaybackState.Playing, windows.Playback);
        practice.StopCommand.Execute(null);
        Assert.Equal(PlaybackState.Stopped, windows.Playback);
        await main.Settings.SaveCommand.ExecuteAsync();
        Assert.Empty(main.Settings.Error);
        Assert.DoesNotContain("PronunciationProvider", File.ReadAllText(Path.Combine(data.DirectoryPath, "settings.json")));
    }

    [Fact]
    public void LocalExamplePlaybackCancelsRecordingAndPausesReplayWithoutStartingAnAiVoice()
    {
        var windows = new PracticeFixtures.Speech();
        var reading = new ReadingPracticeViewModel(new(), "unused", windows.Stop, new(), windows);
        var example = new ReviewExampleViewModel(new("Please confirm your appointment.", "請確認預約。"), reading);
        var actions = new List<string>();
        reading.Requested += (action, _) => actions.Add(action);
        reading.SetExamples([example]);
        reading.SetAnswerVisible(true);
        reading.Ready();
        reading.RecordCommand.Execute(null);
        reading.Recording(reading.Run);
        reading.PronounceCommand.Execute(example.English);
        Assert.Equal(ReadingPhase.Finishing, reading.Phase);
        Assert.Equal(new[] { "cancel", "pause-playback" }, actions.TakeLast(2));
        Assert.Equal(example.English, windows.Spoken);
        reading.Cancelled(reading.Run);
        reading.RecordCommand.Execute(null);
        Assert.Equal(ReadingPhase.Starting, reading.Phase);
        Assert.Equal(PlaybackState.Stopped, windows.Playback);
    }
}
