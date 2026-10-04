using System.Text.Json;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class PracticeReliabilityTests
{
    [Fact]
    public async Task CancellationRejectsLateGenerationResultPreservesWorkAndAllowsRetry()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        var generator = new DelayedGenerator(); var settings = new AppSettings(); var saves = 0;
        var vm = new PracticeViewModel(data.Store, data.Store, generator, new PracticeFixtures.Speech(), settings,
            () => saves++, _ => { }, () => true, data.Clock);
        await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
        vm.Questions[0].SelectedIndex = 2;
        var previous = vm.Questions; var passage = vm.Passage;
        vm.ToggleOptionsCommand.Execute(null); vm.QuestionCount = 5; generator.Delay = true;
        var pending = vm.GenerateCommand.ExecuteAsync();
        var request = await generator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsBusy); Assert.False(vm.ListeningCommand.CanExecute(null)); Assert.False(vm.ToggleOptionsCommand.CanExecute(null));
        await vm.GenerateCommand.ExecuteAsync(); // A second click must not start another request.
        Assert.Equal(2, generator.Calls);
        vm.CancelCommand.Execute(null);
        generator.Completion.SetResult(PracticeFixtures.Material(request)); // Simulate a provider ignoring cancellation.
        await pending;
        Assert.Contains("已取消", vm.Error); Assert.False(vm.IsBusy); Assert.False(vm.GenerateCommand.IsRunning);
        Assert.Same(previous, vm.Questions); Assert.Equal(2, vm.Questions[0].SelectedIndex); Assert.Equal(passage, vm.Passage);
        Assert.True(vm.IsChoosingOptions); Assert.Equal(1, saves); Assert.Equal(4, settings.Practice.QuestionCount);
        Assert.Empty(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        generator.Delay = false;
        await vm.GenerateCommand.ExecuteAsync(); Assert.Empty(vm.Error);
        Assert.Equal(5, vm.Questions.Length); Assert.Equal(2, saves); Assert.False(vm.IsChoosingOptions);
        Assert.True(vm.ListeningCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConflictingCompletionCannotOverwritePreviouslySavedMetadata()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        var original = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Reading, ["商業"], [word.Id]);
        await data.Store.CompletePracticeAsync(original);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.CompletePracticeAsync(original with
            { Mode = PracticeMode.Listening, Topics = ["旅行"] }));
        var saved = Assert.Single(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        Assert.Equal(original.Mode, saved.Mode); Assert.Equal(original.Topics, saved.Topics);
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task ThreeMonthRetentionKeepsExactCalendarBoundaryAndRemovesOlderRecords()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        data.Clock.Now = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
        var boundary = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
        var retained = new PracticeCompletion(Guid.NewGuid(), boundary, PracticeMode.Reading, ["商業"], [word.Id]);
        var expired = retained with { Id = Guid.NewGuid(), CompletedAt = boundary.AddMilliseconds(-1) };
        await data.Store.CompletePracticeAsync(retained); await data.Store.CompletePracticeAsync(expired);
        var history = await data.Store.GetPracticeHistoryAsync(data.Clock.Now);
        Assert.Equal(retained.Id, Assert.Single(history).Id);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM practice_sessions;"));
    }

    [Fact]
    public void PracticeSettingsRoundTripAndInvalidOptionsCannotOverwriteSavedPreferences()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings { Practice = new() { Mode = PracticeMode.Listening, Kind = PassageKind.Monologue,
            Length = PracticeLength.Long, Level = PracticeLevel.Hard, Density = WordDensity.High,
            QuestionCount = 5, SpeechRate = -2, Topics = ["商業", "旅行"] } };
        settings.Save(data.DirectoryPath);
        Assert.Equal(JsonSerializer.Serialize(settings.Practice), JsonSerializer.Serialize(AppSettings.Load(data.DirectoryPath).Practice));
        var path = Path.Combine(data.DirectoryPath, "settings.json"); var original = File.ReadAllText(path);
        foreach (var invalid in new[] { settings.Practice with { Kind = PassageKind.Email }, settings.Practice with { QuestionCount = 6 },
            settings.Practice with { SpeechRate = 3 }, settings.Practice with { Topics = null! } })
        {
            settings.Practice = invalid;
            Assert.Throws<ArgumentException>(() => settings.Save(data.DirectoryPath));
            Assert.Equal(original, File.ReadAllText(path));
        }
    }

    private sealed class DelayedGenerator : IPracticeGenerator
    {
        public bool Delay { get; set; }
        public int Calls { get; private set; }
        public TaskCompletionSource<PracticeRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PracticeMaterial> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PracticeMaterial> GeneratePracticeAsync(PracticeRequest request, CancellationToken token = default)
        {
            Calls++;
            if (!Delay) return Task.FromResult(PracticeFixtures.Material(request));
            Started.TrySetResult(request);
            return Completion.Task;
        }
    }
}
