using System.Text.Json;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class PracticeTests
{
    [Fact]
    public async Task PracticeTopicsCountCompletionsAcrossModesAndIncludeZeroCountTopicsInLastThreeMonths()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        await data.Store.AddCategoryAsync("未練習");
        async Task Complete(PracticeMode mode, string[] topics, DateTimeOffset date) =>
            await data.Store.CompletePracticeAsync(new(Guid.NewGuid(), date, mode, topics, [word.Id]));
        await Complete(PracticeMode.Reading, ["商業", "旅行", "旅行"], data.Clock.Now);
        await Complete(PracticeMode.Listening, ["商業"], data.Clock.Now.AddDays(-1));
        await Complete(PracticeMode.Reading, ["商業"], data.Clock.Now.AddMonths(-4));
        await Complete(PracticeMode.Listening, ["歷史主題"], data.Clock.Now.AddDays(-2));
        var vm = new PracticeViewModel(data.Store, data.Store, new PracticeFixtures.Generator(), new PracticeFixtures.Speech(), new(), () => { }, _ => { }, clock: data.Clock);
        await vm.LoadAsync();
        Assert.Equal(3, vm.CompletedCount);
        var business = vm.TopicActivities.Single(x => x.Name == "商業");
        var travel = vm.TopicActivities.Single(x => x.Name == "旅行");
        var zero = vm.TopicActivities.Single(x => x.Name == "未練習");
        Assert.Equal(2, business.Count); Assert.Equal(1, business.ReadingCount); Assert.Equal(1, business.ListeningCount);
        Assert.Equal(1, travel.Count); Assert.Equal(0, zero.Count); Assert.Null(zero.LastCompletedAt);
        Assert.Contains(vm.TopicActivities, x => x.Name == "歷史主題" && x.Count == 1);
        Assert.NotEqual(zero.Color, travel.Color); Assert.NotEqual(travel.Color, business.Color);
        Assert.Equal(4, vm.TopicActivities.Sum(x => x.Count));
        Assert.Contains("共 3 次", vm.HistorySummary);
    }

    [Fact]
    public async Task BacklogRestrictionExplainsWhyUnlearnedWordCannotBeRated()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        var learned = Enumerable.Range(0, 20).Select(i => StudyTestData.Word($"learned{i}")).ToArray();
        var unlearned = StudyTestData.Word("unlearned");
        await data.Store.SaveVocabularyBatchAsync([.. learned, unlearned]);
        await data.Store.GetNextReviewAsync(data.Clock.Now, 30);
        foreach (var word in learned)
            await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        data.Clock.Now = data.Clock.Now.AddDays(1);
        var completion = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Reading, ["商業"], [unlearned.Id]);
        await data.Store.CompletePracticeAsync(completion);
        var eligibility = Assert.Single(await data.Store.GetPracticeEligibilityAsync(completion.Id, data.Clock.Now, 30));
        Assert.True(eligibility.IsNewLimitBlocked); Assert.False(eligibility.CanRate);
        Assert.Contains("今日上限 0 個", eligibility.Message); Assert.Contains("到期複習較多", eligibility.Message);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.RatePracticeAsync(completion.Id,
            new(unlearned.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), eligibility.Version), 30));
    }

    [Fact]
    public async Task CompletionIsIdempotentAndRatingsShareQuotaWithoutEarlyOrDuplicateReviews()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        var first = await data.AddWordAsync(); var second = await data.AddWordAsync("invoice");
        var exercise = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Reading, ["商業"], [first.Id, second.Id]);
        await data.Store.CompletePracticeAsync(exercise); await data.Store.CompletePracticeAsync(exercise);
        Assert.Single(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        var eligibility = await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 1);
        Assert.All(eligibility, x => Assert.True(x.CanRate));
        var review = new ReviewSubmission(first.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), eligibility[0].Version);
        var result = await data.Store.RatePracticeAsync(exercise.Id, review, 1);
        Assert.Equal(result, await data.Store.RatePracticeAsync(exercise.Id, review, 1));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        var after = await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 1);
        Assert.All(after, x => Assert.False(x.CanRate));
        Assert.Contains("名額", after.Single(x => x.SenseId == second.Id).Message);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.RatePracticeAsync(exercise.Id,
            new(second.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0), 1));
        var next = exercise with { Id = Guid.NewGuid() };
        await data.Store.CompletePracticeAsync(next);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.RatePracticeAsync(next.Id, review, 1));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.RatePracticeAsync(next.Id,
            review with { OperationId = Guid.NewGuid(), ExpectedScheduleVersion = 1 }, 1));
        data.Clock.Now = result.DueAt;
        Assert.True((await data.Store.GetPracticeEligibilityAsync(next.Id, data.Clock.Now, 1)).Single(x => x.SenseId == first.Id).CanRate);
        await data.Store.RatePracticeAsync(next.Id, review with { OperationId = Guid.NewGuid(), ExpectedScheduleVersion = 1, ReviewedAt = data.Clock.Now }, 1);
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task FailedPracticeRatingRollsBackAndUndoRestoresEligibilityButKeepsNewSlot()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        var exercise = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Listening, ["商業"], [word.Id]);
        data.FailAt = "Practice.BeforeCommit";
        await Assert.ThrowsAsync<IOException>(() => data.Store.CompletePracticeAsync(exercise));
        Assert.Empty(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        data.FailAt = null; await data.Store.CompletePracticeAsync(exercise);
        var review = new ReviewSubmission(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), 0);
        data.FailAt = "Review.AfterSchedule";
        await Assert.ThrowsAsync<IOException>(() => data.Store.RatePracticeAsync(exercise.Id, review, 1));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM practice_ratings;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        data.FailAt = null; await data.Store.RatePracticeAsync(exercise.Id, review, 1);
        await data.Store.UndoReviewAsync(review.OperationId);
        Assert.True(Assert.Single(await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 0)).CanRate);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM practice_ratings;"));
        await data.Store.RatePracticeAsync(exercise.Id, review with { OperationId = Guid.NewGuid(), ExpectedScheduleVersion = 2 }, 1);
    }

    [Fact]
    public async Task BackupRestoresPracticeMetadataAndRetentionDoesNotDeleteFsrsReviews()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        var exercise = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Listening, ["商業"], [word.Id]);
        await data.Store.CompletePracticeAsync(exercise);
        await data.Store.RatePracticeAsync(exercise.Id, new(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), 0), 5);
        var backup = new BackupService(data.Store); var path = Path.Combine(data.DirectoryPath, "practice.zip");
        await backup.CreateBackupAsync(path);
        data.Execute("DELETE FROM practice_sessions;");
        await backup.RestoreBackupAsync(path);
        Assert.Equal(exercise.Id, Assert.Single(await data.Store.GetPracticeHistoryAsync(data.Clock.Now)).Id);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM practice_ratings;"));
        data.Clock.Now = data.Clock.Now.AddMonths(3).AddDays(1);
        Assert.Empty(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM practice_ratings;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task VersionTwoMigrationIsAtomicAndPreservesExistingProgress()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), 0));
        data.Execute("DROP TABLE practice_ratings; DROP TABLE practice_sessions; PRAGMA user_version=2;");
        data.FailAt = "Migration.BeforeCommit";
        await Assert.ThrowsAsync<IOException>(() => data.NewStore().InitializeAsync());
        Assert.Equal(2L, data.Scalar("PRAGMA user_version;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='practice_sessions';"));
        data.FailAt = null; await data.NewStore().InitializeAsync();
        Assert.Equal(3L, data.Scalar("PRAGMA user_version;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public void SelectionWeightsStarsAndRecentExposureWithoutForcingThemEveryTime()
    {
        var now = DateTimeOffset.UtcNow;
        var pool = Enumerable.Range(0, 40).Select(i => new PracticeCandidate(StudyTestData.Word($"word{i}") with { IsStarred = i < 20 },
            new(Guid.NewGuid(), LearningState.Learning, 0, null, null, now, null))).ToArray();
        var selector = new PracticeSelector(new Random(42)); var options = new PracticeOptions { Topics = ["商業"] };
        var selections = Enumerable.Range(0, 500).Select(_ => selector.Select(options, pool, [], now)).ToArray();
        var starred = selections.SelectMany(x => x.Targets).Count(x => x.IsStarred);
        Assert.InRange(starred, 1600, 2150);
        Assert.All(selections, x => Assert.Equal(6, x.Targets.DistinctBy(w => w.Id).Count()));
        Assert.Contains(selections, x => !x.Targets.Any(w => w.Id == pool[0].Word.Id));
        var history = new[] { new PracticeCompletion(Guid.NewGuid(), now, PracticeMode.Reading, ["商業"], pool.Take(20).Select(x => x.Word.Id).ToArray()) };
        var recentSelections = Enumerable.Range(0, 500).SelectMany(_ => selector.Select(options, pool, history, now).Targets).Count(x => x.IsStarred);
        Assert.True(recentSelections < starred / 2);
        Assert.Throws<InvalidOperationException>(() => selector.Select(options with { Topics = ["不存在"] }, pool, [], now));
    }

    [Fact]
    public void ParserRequiresEnglishQuestionsExactEvidenceAndTargetCoverage()
    {
        var request = new PracticeRequest(new() { QuestionCount = 3 }, [StudyTestData.Word()]);
        var material = PracticeFixtures.Material(request);
        PracticeMaterial Parse(PracticeMaterial value) => CodexContentGenerator.ParsePracticeResponse(JsonSerializer.Serialize(value), request);
        Assert.Equal(material.Passage, Parse(material).Passage);
        Assert.Throws<InvalidDataException>(() => Parse(material with { Passage = "Too short." }));
        Assert.Throws<InvalidDataException>(() => Parse(material with { Targets = [] }));
        var question = material.Questions[0];
        foreach (var invalid in new[] { question with { Evidence = "Invented evidence." }, question with { Prompt = "中文題目" },
            question with { AnswerIndex = 4 }, question with { Options = ["Same", "Same", "C", "D"] } })
            Assert.Throws<InvalidDataException>(() => Parse(material with { Questions = [invalid, .. material.Questions.Skip(1)] }));
        Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParsePracticeResponse(JsonSerializer.Serialize(material), request with { Options = request.Options with { Mode = PracticeMode.Listening } }));
    }

    [Fact]
    public async Task GeneratorUsesEditablePromptsAndExplicitWordRangeWithSharedDailyQuota()
    {
        using var data = new StudyTestData();
        var request = new PracticeRequest(new() { QuestionCount = 3, Kind = PassageKind.Story }, [StudyTestData.Word()]);
        var runner = new PracticeRunner(PracticeFixtures.Material(request));
        var generator = new CodexContentGenerator(new() { DailyGenerationLimit = 1 }, data.DirectoryPath, runner);
        await generator.GeneratePracticeAsync(request);
        Assert.Contains("scenario-practice-v1", runner.Input);
        Assert.Contains("\"minimum\":120,\"maximum\":180", runner.Input);
        Assert.Contains("Story", runner.Input);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Prompts", "practice-listening.txt")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(request.Targets[0]));
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("purpose")]
    [InlineData("inference")]
    public void ReadingComprehensionSubtypesAreAcceptedWithoutDroppingVocabularyOrGrammar(string kind)
    {
        var request = new PracticeRequest(new(), [StudyTestData.Word()]);
        var material = PracticeFixtures.Material(request);
        material = material with { Questions = material.Questions.Select(q => q.Kind == "comprehension" ? q with { Kind = kind } : q).ToArray() };
        var result = CodexContentGenerator.ParsePracticeResponse(JsonSerializer.Serialize(material), request);
        Assert.Equal(4, result.Questions.Length);
        Assert.Equal("comprehension", result.Questions[0].Kind);
        Assert.Contains(result.Questions, q => q.Kind == "vocabulary");
        Assert.Contains(result.Questions, q => q.Kind == "grammar");
        var unsupported = material with { Questions = [material.Questions[0] with { Kind = "unknown" }, .. material.Questions.Skip(1)] };
        var error = Assert.Throws<InvalidDataException>(() => CodexContentGenerator.ParsePracticeResponse(JsonSerializer.Serialize(unsupported), request));
        Assert.Contains("第 1 題", error.Message);
    }

    [Fact]
    public void DefaultReadingResponseThatPreviouslyFailedValidationIsAccepted()
    {
        // Captured from a real generation with six synthetic words and the default four questions.
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "practice-reading-default.json")));
        var request = new PracticeRequest(new(), fixture.RootElement.GetProperty("targets").Deserialize<VocabularyItem[]>(VocabularyFileService.JsonOptions)!);
        var material = CodexContentGenerator.ParsePracticeResponse(fixture.RootElement.GetProperty("material").GetRawText(), request);
        Assert.Equal(4, material.Questions.Length);
        Assert.Equal(6, material.Targets.Length);
        Assert.Equal("comprehension", material.Questions[3].Kind);
        Assert.Contains(material.Questions[3].Evidence, material.Passage);
    }

    [Theory]
    [InlineData(PracticeMode.Reading, 3)]
    [InlineData(PracticeMode.Reading, 4)]
    [InlineData(PracticeMode.Reading, 5)]
    [InlineData(PracticeMode.Listening, 3)]
    [InlineData(PracticeMode.Listening, 4)]
    [InlineData(PracticeMode.Listening, 5)]
    public async Task GenerationSchemaMatchesModeQuestionCountAndSixTargetIds(PracticeMode mode, int count)
    {
        using var data = new StudyTestData();
        var request = new PracticeRequest(new() { Mode = mode, QuestionCount = count }, Enumerable.Range(0, 6).Select(_ => StudyTestData.Word()).ToArray());
        var runner = new PracticeRunner(PracticeFixtures.Material(request));
        await new CodexContentGenerator(new(), data.DirectoryPath, runner).GeneratePracticeAsync(request);
        using var document = JsonDocument.Parse(runner.Schema);
        var properties = document.RootElement.GetProperty("properties");
        var questions = properties.GetProperty("questions");
        Assert.Equal(count, questions.GetProperty("minItems").GetInt32());
        Assert.Equal(count, questions.GetProperty("maxItems").GetInt32());
        var kinds = questions.GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(mode == PracticeMode.Reading ? new[] { "comprehension", "vocabulary", "grammar" } : ["comprehension", "inference", "purpose", "detail"], kinds);
        var targets = properties.GetProperty("targets");
        Assert.Equal(6, targets.GetProperty("minItems").GetInt32());
        Assert.Equal(6, targets.GetProperty("maxItems").GetInt32());
        Assert.Equal(request.Targets.Select(x => x.Id), targets.GetProperty("items").GetProperty("properties").GetProperty("senseId").GetProperty("enum").EnumerateArray().Select(x => x.GetGuid()));
        using var input = JsonDocument.Parse(runner.Input.Split("INPUT DATA:\n")[1]);
        Assert.Equal(count, input.RootElement.GetProperty("options").GetProperty("questionCount").GetInt32());
    }

    [Fact]
    public async Task ListeningRetainsAnswersUntilSubmissionAndRatingsAreExplicit()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); await data.AddWordAsync();
        var generator = new PracticeFixtures.Generator(); var speech = new PracticeFixtures.Speech();
        var settings = new AppSettings { DailyNewLimit = 1, Practice = new() { Mode = PracticeMode.Listening, QuestionCount = 3 } };
        var vm = new PracticeViewModel(data.Store, data.Store, generator, speech, settings, () => { }, _ => { }, () => false, data.Clock);
        await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync();
        Assert.Empty(vm.Error); Assert.True(vm.HasMaterial); Assert.Empty(vm.Passage); Assert.Empty(vm.Translation);
        Assert.Empty(vm.Targets); Assert.All(vm.Questions, q => Assert.Empty(q.Explanation));
        Assert.False(vm.SubmitCommand.CanExecute(null));
        vm.PlayCommand.Execute(null); Assert.Equal(generator.Last!.Passage, speech.Spoken);
        vm.PauseCommand.Execute(null); Assert.Equal(PlaybackState.Paused, speech.Playback);
        vm.ResumeCommand.Execute(null); Assert.Equal(PlaybackState.Playing, speech.Playback);
        foreach (var question in vm.Questions) question.SelectedIndex = 0;
        var original = vm.Questions;
        vm.StopSpeech(); await vm.LoadAsync(); Assert.Same(original, vm.Questions);
        vm.ReadingCommand.Execute(null); Assert.True(vm.IsListening); Assert.Same(original, vm.Questions);
        await vm.GenerateCommand.ExecuteAsync(); Assert.Same(original, vm.Questions);
        data.FailAt = "Practice.BeforeCommit";
        await vm.SubmitCommand.ExecuteAsync(); Assert.False(vm.IsSubmitted); Assert.Empty(vm.Passage);
        Assert.False(vm.Questions[0].CanAnswer);
        vm.Questions[0].SelectedIndex = 1; Assert.Equal(0, vm.Questions[0].SelectedIndex);
        data.FailAt = null; await vm.SubmitCommand.ExecuteAsync(); Assert.Empty(vm.Error);
        Assert.True(vm.IsSubmitted); Assert.NotEmpty(vm.Passage); Assert.NotEmpty(vm.Translation);
        Assert.Single(await data.Store.GetPracticeHistoryAsync(data.Clock.Now));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        var target = Assert.Single(vm.Targets); await target.GoodCommand.ExecuteAsync(); Assert.Empty(vm.Error);
        Assert.False(target.GoodCommand.CanExecute(null));
        await vm.UndoCommand.ExecuteAsync(); Assert.Empty(vm.Error); Assert.True(target.GoodCommand.CanExecute(null));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
    }

    [Fact]
    public async Task GenerationFailureKeepsPreviousWorkAndReviewPageChangesRecheckEligibility()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync(); var word = await data.AddWordAsync();
        var generator = new PracticeFixtures.Generator();
        var vm = new PracticeViewModel(data.Store, data.Store, generator, new PracticeFixtures.Speech(), new(), () => { }, _ => { }, () => true, data.Clock);
        await vm.LoadAsync(); await vm.GenerateCommand.ExecuteAsync(); var oldQuestions = vm.Questions;
        generator.Fail = true; await vm.GenerateCommand.ExecuteAsync();
        Assert.Same(oldQuestions, vm.Questions); Assert.NotEmpty(vm.Error);
        foreach (var q in vm.Questions) q.SelectedIndex = 0;
        await vm.SubmitCommand.ExecuteAsync();
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), 0));
        await Assert.Single(vm.Targets).AgainCommand.ExecuteAsync();
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Contains("下次複習", vm.Targets[0].Status);
    }

    [Theory]
    [InlineData(PracticeMode.Reading)]
    [InlineData(PracticeMode.Listening)]
    public async Task ReadingAndListeningGenerationUseSelectedSpeedTier(PracticeMode mode)
    {
        using var data = new StudyTestData();
        var request = new PracticeRequest(new() { Mode = mode }, [StudyTestData.Word()]);
        var runner = new PracticeRunner(PracticeFixtures.Material(request));
        await new CodexContentGenerator(new() { ServiceTier = "priority" }, data.DirectoryPath, runner).GeneratePracticeAsync(request);
        Assert.Contains("service_tier=\"priority\"", runner.Arguments);
        Assert.Contains("forced_login_method=\"chatgpt\"", runner.Arguments);
    }

    private sealed class PracticeRunner(PracticeMaterial material) : ICodexProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string Input { get; private set; } = "";
        public string Schema { get; private set; } = "";
        public Task<CodexRunResult> RunAsync(string executable, IReadOnlyList<string> arguments, string input, string directory, TimeSpan timeout, CancellationToken token)
        {
            if (arguments[0] == "--version") return Task.FromResult(new CodexRunResult(0, "codex-cli 0.153.4", ""));
            if (arguments[0] == "login") return Task.FromResult(new CodexRunResult(0, "", "Logged in using ChatGPT"));
            Input = input;
            Arguments = arguments;
            Schema = File.ReadAllText(arguments[arguments.ToList().IndexOf("--output-schema") + 1]);
            return Task.FromResult(new CodexRunResult(0, JsonSerializer.Serialize(new { type = "item.completed", item = new { type = "agent_message", text = JsonSerializer.Serialize(material) } }) + "\n{\"type\":\"turn.completed\"}", ""));
        }
    }
}

internal static class PracticeFixtures
{
    internal static PracticeMaterial Material(PracticeRequest request)
    {
        var passage = "Please confirm your appointment today. " + string.Join(" ", Enumerable.Repeat("Our office will open early tomorrow to help every visitor prepare for the upcoming business meeting.", 8));
        var kinds = request.Options.Mode == PracticeMode.Reading ? new[] { "comprehension", "vocabulary", "grammar", "comprehension", "vocabulary" } : ["detail", "purpose", "inference", "comprehension", "detail"];
        return new("A surprising appointment", passage, "請確認您的預約。辦公室明天將提早開門，協助訪客準備會議。",
            Enumerable.Range(0, request.Options.QuestionCount).Select(i => new PracticeQuestion(kinds[i], $"What is the best answer for question {i + 1}?", ["The office", "A hotel", "A museum", "A train"], 0,
                "文章說明辦公室會提早開門。", "Our office will open early tomorrow", ["符合文章。", "文章未提及旅館。", "文章未提及博物館。", "文章未提及火車。"]) ).ToArray(),
            request.Targets.Select(x => new PracticeUsage(x.Id, x.Headword, x.Meaning, "Please confirm your appointment today.")).ToArray());
    }
    internal sealed class Generator : IPracticeGenerator, IContentGenerator
    {
        public PracticeMaterial? Last { get; private set; }
        public bool Fail { get; set; }
        public Task<string> CheckAvailabilityAsync(CancellationToken token = default) => Task.FromResult("test");
        public Task<AiEnrichment> GenerateAsync(VocabularyItem word, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PracticeMaterial> GeneratePracticeAsync(PracticeRequest request, CancellationToken token = default)
        { if (Fail) throw new IOException("生成失敗"); Last = Material(request); return Task.FromResult(Last); }
    }
    internal sealed class Speech : IPracticeSpeech, IPronunciationService
    {
        public bool IsAvailable => true;
        public string Status => "test";
        public void Speak(string text) => Play(text, 0);
        public void Dispose() => Stop();
        public PlaybackState Playback { get; private set; }
        public string Spoken { get; private set; } = "";
        public event Action? PlaybackChanged;
        public void Play(string text, int rate) { Spoken = text; Set(PlaybackState.Playing); }
        public void Pause() => Set(PlaybackState.Paused);
        public void Resume() => Set(PlaybackState.Playing);
        public void Stop() => Set(PlaybackState.Stopped);
        private void Set(PlaybackState state) { Playback = state; PlaybackChanged?.Invoke(); }
    }
}
