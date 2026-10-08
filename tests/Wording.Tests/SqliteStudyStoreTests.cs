using Wording.Core;
using Wording.Infrastructure;
using Xunit;

namespace Wording.Tests;

public sealed class SqliteStudyStoreTests
{
    [Fact]
    public async Task SeedReimportPreservesEditsSelectionAndSchedule()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word();
        await data.Store.ImportSeedPackAsync(new("sample", 1, [word]));
        var imported = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal(Enrollment.Selected, imported.Enrollment);
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var operation = new ReviewSubmission(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        var result = await data.Store.SubmitReviewAsync(operation);
        var selected = Assert.Single(await data.Store.GetVocabularyAsync());
        await data.Store.SaveVocabularyAsync(selected with { Meaning = "自己修改的解釋" });
        await data.Store.ImportSeedPackAsync(new("sample", 2, [word with { Meaning = "新版教材" }]));
        var actual = Assert.Single(await data.NewStore().GetVocabularyAsync());
        Assert.Equal("自己修改的解釋", actual.Meaning);
        Assert.Equal(Enrollment.Selected, actual.Enrollment);
        Assert.True(actual.IsUserEdited);
        Assert.Equal(result.DueAt.ToUnixTimeMilliseconds(), Convert.ToInt64(data.Scalar("SELECT due_ms FROM cards;")));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task UneditedSeedUpdateChangesContentButNotFlagsOrStableIds()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word();
        await data.Store.ImportSeedPackAsync(new("sample", 1, [word]));
        await data.Store.SetEnrollmentAsync(word.Id, Enrollment.Selected);
        await data.Store.SetPausedAsync(word.Id, true);
        var cardId = data.Scalar("SELECT card_id FROM cards;");
        await data.Store.ImportSeedPackAsync(new("sample", 2, [word with { Meaning = "教材勘誤" }]));
        var actual = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal("教材勘誤", actual.Meaning);
        Assert.True(actual.IsPaused);
        Assert.Equal(Enrollment.Selected, actual.Enrollment);
        Assert.Equal(cardId, data.Scalar("SELECT card_id FROM cards;"));
    }

    [Fact]
    public async Task EditorSavePersistsSelectionAndPauseFlagsAndInvalidatesStaleQueue()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var item = StudyTestData.Word() with { Enrollment = Enrollment.Candidate };
        await data.Store.SaveVocabularyAsync(item);
        Assert.NotNull(await data.Store.GetNextReviewAsync(data.Clock.Now, 5));
        await data.Store.SaveVocabularyAsync(item with { Enrollment = Enrollment.Selected });
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SaveVocabularyAsync(card.Word with { IsPaused = true });
        Assert.True(Assert.Single(await data.Store.GetVocabularyAsync()).IsPaused);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(
            new(item.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion)));
        await data.Store.SaveVocabularyAsync(card.Word with { IsArchived = true });
        Assert.Empty(await data.Store.GetVocabularyAsync());
    }

    [Fact]
    public async Task ArchivedWordsCanBeFoundAndRestoredWithoutResettingProgress()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var result = await data.Store.SubmitReviewAsync(
            new(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        await data.Store.SetPausedAsync(word.Id, true);
        var cardId = data.Scalar("SELECT card_id FROM cards;");
        var stability = data.Scalar("SELECT stability FROM cards;");
        var difficulty = data.Scalar("SELECT difficulty FROM cards;");

        await data.Store.ArchiveAsync(word.Id);
        Assert.Empty(await data.Store.GetVocabularyAsync());
        var archived = Assert.Single(await data.Store.GetVocabularyAsync(
            search: "appointment", category: "商業", includeArchived: true));
        Assert.True(archived.IsArchived);
        Assert.Empty(await data.Store.GetVocabularyAsync(search: "missing", includeArchived: true));
        Assert.Empty(await data.Store.GetVocabularyAsync(category: "missing", includeArchived: true));

        await data.Store.SaveVocabularyAsync(archived with { IsArchived = false });
        var restored = Assert.Single(await data.NewStore().GetVocabularyAsync());
        Assert.False(restored.IsArchived);
        Assert.True(restored.IsPaused);
        Assert.Equal(Enrollment.Selected, restored.Enrollment);
        Assert.Equal(cardId, data.Scalar("SELECT card_id FROM cards;"));
        Assert.Equal(stability, data.Scalar("SELECT stability FROM cards;"));
        Assert.Equal(difficulty, data.Scalar("SELECT difficulty FROM cards;"));
        Assert.Equal(result.DueAt.ToUnixTimeMilliseconds(), data.Scalar("SELECT due_ms FROM cards;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Null(await data.Store.GetNextReviewAsync(result.DueAt, 5));

        await data.Store.SetPausedAsync(word.Id, false);
        var due = (await data.Store.GetNextReviewAsync(result.DueAt, 5))!;
        Assert.Equal(word.Id, due.Word.Id);
        Assert.False(due.IsNew);
    }

    [Fact]
    public async Task SameSenseInTwoCategoriesHasOnlyOneCardAndDifferentMeaningsAreIndependent()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var bank = StudyTestData.Word("bank", "銀行");
        var river = StudyTestData.Word("bank", "河岸") with { Cue = "beside a river" };
        await data.Store.SaveVocabularyAsync(bank);
        await data.Store.SaveVocabularyAsync(river);
        Assert.Equal(2, (await data.Store.GetVocabularyAsync(category: "旅行")).Count);
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        var second = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        Assert.NotEqual(card.Word.Id, second.Word.Id);
        Assert.True(second.IsNew);
    }

    [Fact]
    public async Task DuplicateOperationIsIdempotentAndStaleVersionCannotOverwrite()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var submission = new ReviewSubmission(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        var results = await Task.WhenAll(data.Store.SubmitReviewAsync(submission), data.Store.SubmitReviewAsync(submission));
        Assert.Equal(results[0], results[1]);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(submission with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(submission with { Rating = ReviewRating.Easy }));
    }

    [Fact]
    public async Task FailureBetweenScheduleAndLogRollsBackQuotaAndSchedule()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var submission = new ReviewSubmission(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        data.FailAt = "Review.AfterSchedule";
        await Assert.ThrowsAsync<IOException>(() => data.Store.SubmitReviewAsync(submission));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal(0L, data.Scalar("SELECT version FROM cards;"));
        data.FailAt = null;
        await data.Store.SubmitReviewAsync(submission);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task UndoRestoresScheduleButVersionAndDailyQuotaDoNotGoBackwards()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var before = data.Scalar("SELECT due_ms FROM cards;");
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 1))!;
        var submission = new ReviewSubmission(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        await data.Store.SubmitReviewAsync(submission);
        await data.Store.UndoReviewAsync(submission.OperationId);
        Assert.Equal(before, data.Scalar("SELECT due_ms FROM cards;"));
        Assert.Equal(2L, data.Scalar("SELECT version FROM cards;"));
        Assert.Equal(1L, data.Scalar("SELECT undone FROM review_log;"));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Equal(0, (await data.Store.GetDashboardAsync(data.Clock.Now)).ReviewedToday);
        var retry = (await data.Store.GetNextReviewAsync(data.Clock.Now, 1))!;
        Assert.Equal(word.Id, retry.Word.Id);
        Assert.True(retry.IsNew);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(submission));
        await data.Store.SubmitReviewAsync(submission with { OperationId = Guid.NewGuid(), ExpectedScheduleVersion = retry.ScheduleVersion });
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
    }

    [Fact]
    public async Task UndoFailureRollsBackAndUndoCannotCrossRestartOrChangedCard()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var submission = new ReviewSubmission(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        await data.Store.SubmitReviewAsync(submission);
        data.FailAt = "Undo.AfterSchedule";
        await Assert.ThrowsAsync<IOException>(() => data.Store.UndoReviewAsync(submission.OperationId));
        Assert.Equal(1L, data.Scalar("SELECT version FROM cards;"));
        Assert.Equal(0L, data.Scalar("SELECT undone FROM review_log;"));
        data.FailAt = null;
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.NewStore().UndoReviewAsync(submission.OperationId));
        await data.Store.SetPausedAsync(word.Id, true);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.UndoReviewAsync(submission.OperationId));
    }

    [Fact]
    public async Task DailyQuotaSurvivesRestartAndResetsByCalendarDay()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        for (var index = 0; index < 3; index++) await data.AddWordAsync("word" + index);
        for (var index = 0; index < 2; index++)
        {
            var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 2))!;
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        }
        var restarted = data.NewStore();
        await restarted.InitializeAsync();
        Assert.Null(await restarted.GetNextReviewAsync(data.Clock.Now, 2));
        Assert.Equal(2, (await restarted.GetDashboardAsync(data.Clock.Now)).StartedToday);
        data.Clock.Now = data.Clock.Now.AddDays(1);
        Assert.NotNull(await restarted.GetNextReviewAsync(data.Clock.Now, 2));
        Assert.Equal(0, (await restarted.GetDashboardAsync(data.Clock.Now)).StartedToday);
    }

    [Fact]
    public async Task LearningCardsAreNotShownBeforeDueAndPausedOrArchivedCardsRejectStaleSubmissions()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var result = await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Again, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now.AddSeconds(59), 5));
        Assert.Equal(result.DueAt, (await data.Store.GetDashboardAsync(data.Clock.Now)).NextDue);
        var due = (await data.Store.GetNextReviewAsync(result.DueAt, 5))!;
        Assert.False(due.IsNew);
        await data.Store.SetPausedAsync(word.Id, true);
        Assert.Null(await data.Store.GetNextReviewAsync(result.DueAt, 5));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(
            new(word.Id, ReviewRating.Good, result.DueAt, Guid.NewGuid(), due.ScheduleVersion)));
        await data.Store.SetPausedAsync(word.Id, false);
        due = (await data.Store.GetNextReviewAsync(result.DueAt, 5))!;
        await data.Store.ArchiveAsync(word.Id);
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(
            new(word.Id, ReviewRating.Good, result.DueAt, Guid.NewGuid(), due.ScheduleVersion)));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(50)]
    public async Task DueCardsArePrioritizedWithoutReducingConfiguredQuota(int dueCount)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        for (var index = 0; index < dueCount + 4; index++) await data.AddWordAsync("word" + index);
        var now = data.Clock.Now.ToUnixTimeMilliseconds();
        // Fixture creates already learned/due cards; no fabricated review actions in production.
        data.Execute($"UPDATE cards SET last_review_ms={now - 86400000},due_ms={now},state=2,step=NULL,stability=2,difficulty=5 " +
            $"WHERE sense_id IN (SELECT id FROM senses ORDER BY rowid LIMIT {dueCount});");
        for (var index = 0; index < dueCount; index++)
        {
            var due = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, 3))!;
            Assert.False(due.IsNew);
            Assert.Equal(0, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
            await data.Store.SubmitReviewAsync(new(due.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), due.ScheduleVersion));
        }
        for (var index = 0; index < 3; index++)
        {
            var card = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, 3))!;
            Assert.True(card.IsNew);
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        }
        Assert.Null(await data.NewStore().GetNextReviewAsync(data.Clock.Now, 3));
        Assert.Equal(3, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Equal(DBNull.Value, data.Scalar("SELECT adaptive_limit FROM daily_limits;"));
        Assert.Equal("3", data.Scalar("SELECT configured_limit FROM daily_limits;"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PreviouslyFrozenQuotaIsIgnoredImmediatelyWithoutResettingProgress(int oldAdaptiveLimit)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var words = new List<VocabularyItem>();
        for (var index = 0; index < 5; index++) words.Add(await data.AddWordAsync("word" + index));
        var first = (await data.Store.GetNextReviewAsync(data.Clock.Now, 3))!;
        await data.Store.SubmitReviewAsync(new(first.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), first.ScheduleVersion));
        data.Execute($"UPDATE daily_limits SET adaptive_limit={oldAdaptiveLimit};");

        var restarted = data.NewStore();
        await restarted.InitializeAsync();
        // The submission gate must ignore the old limit even without fetching another card first.
        await restarted.SubmitReviewAsync(new(words[1].Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), 0));
        var third = (await restarted.GetNextReviewAsync(data.Clock.Now, 3))!;
        Assert.True(third.IsNew);
        await restarted.SubmitReviewAsync(new(third.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), third.ScheduleVersion));
        Assert.Null(await restarted.GetNextReviewAsync(data.Clock.Now, 3));
        Assert.Equal(3, (await restarted.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Equal(3L, data.Scalar("SELECT COUNT(*) FROM review_log;"));

        // A same-day setting change is still applied immediately after the upgrade.
        var fourth = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, 4))!;
        Assert.True(fourth.IsNew);
        await data.Store.SubmitReviewAsync(new(fourth.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), fourth.ScheduleVersion));
        Assert.Equal(4, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 4));
    }

    [Fact]
    public async Task LegacyCandidatesAreReviewableWhileSkippedPausedAndArchivedWordsStayExcluded()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var candidate = StudyTestData.Word("candidate") with { Enrollment = Enrollment.Candidate };
        await data.Store.SaveVocabularyAsync(candidate);
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("skipped") with { Enrollment = Enrollment.Skipped });
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("paused") with { IsPaused = true });
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("archived") with { IsArchived = true });
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).NewCount);
        var card = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, 5))!;
        Assert.Equal(candidate.Id, card.Word.Id);
        await data.Store.SubmitReviewAsync(new(candidate.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now.AddDays(30))).DueCount);
    }

    [Fact]
    public async Task TopicFiltersPrioritizeItsDueCardsThenItsNewWordsAndExcludeOtherTopics()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var travel = StudyTestData.Word("travel") with { Categories = ["旅行"] };
        var office = StudyTestData.Word("office") with { Categories = ["職場"] };
        await data.Store.SaveVocabularyAsync(travel);
        await data.Store.SaveVocabularyAsync(office);
        foreach (var category in new[] { "旅行", "職場" })
        {
            var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: category))!;
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        }
        data.Clock.Now = data.Clock.Now.AddDays(30);
        var newOffice = StudyTestData.Word("new office") with { Categories = ["職場"] };
        await data.Store.SaveVocabularyAsync(newOffice);
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("new travel") with { Categories = ["旅行"] });

        var summary = await data.Store.GetDashboardAsync(data.Clock.Now, category: "職場");
        Assert.Equal(1, summary.DueCount);
        Assert.Equal(1, summary.NewCount);
        var due = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: "職場"))!;
        Assert.Equal(office.Id, due.Word.Id);
        Assert.False(due.IsNew);
        var result = await data.Store.SubmitReviewAsync(new(due.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), due.ScheduleVersion));
        Assert.Equal(result.DueAt, (await data.Store.GetDashboardAsync(data.Clock.Now, category: "職場")).NextDue);
        var next = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: "職場"))!;
        Assert.True(next.IsNew);
        Assert.Equal(newOffice.Id, next.Word.Id);
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: "missing' OR 1=1 --"));
        Assert.Equal(0, (await data.Store.GetDashboardAsync(data.Clock.Now, category: "missing")).NewCount);
        Assert.Equal(travel.Id, (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!.Word.Id);
    }

    [Fact]
    public async Task DailyNewLimitIsSharedAcrossTopicsAndCanBeAdjustedWithoutSelectingWords()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("travel") with { Categories = ["旅行"] });
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("office") with { Categories = ["職場"] });
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 1, category: "旅行"))!;
        await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.Null(await data.NewStore().GetNextReviewAsync(data.Clock.Now, 1, category: "職場"));
        Assert.NotNull(await data.Store.GetNextReviewAsync(data.Clock.Now, 2, category: "職場"));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 0, category: "職場"));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
    }

    [Fact]
    public async Task ConfiguredLimitAboveFiveTakesEffectIncludingExistingDailyLimits()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        for (var index = 0; index < 8; index++) await data.AddWordAsync("word" + index);
        await data.Store.GetNextReviewAsync(data.Clock.Now, 5);
        // Simulate a low-workload day created by the previous release.
        data.UseLegacyLimitsSchema();
        await data.NewStore().InitializeAsync();
        for (var index = 0; index < 7; index++)
        {
            var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 7))!;
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        }
        Assert.Equal(7, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Null(await data.NewStore().GetNextReviewAsync(data.Clock.Now, 7));
    }

    [Fact]
    public async Task ArbitrarilyLargeLimitWorksBeyondTenAndSurvivesRestart()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        for (var index = 0; index < 13; index++) await data.AddWordAsync("word" + index);
        var limit = System.Numerics.BigInteger.Parse("99999999999999999999999999999999999999999999999");
        for (var index = 0; index < 12; index++)
        {
            var card = (await data.NewStore().GetNextReviewAsync(data.Clock.Now, limit))!;
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        }
        Assert.Equal(limit.ToString(), data.Scalar("SELECT configured_limit FROM daily_limits;"));
        Assert.NotNull(await data.NewStore().GetNextReviewAsync(data.Clock.Now, limit));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 12));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => data.Store.GetNextReviewAsync(data.Clock.Now, -1));
    }

    [Fact]
    public async Task ExistingDatabaseMigrationRollsBackOnFailureAndKeepsProgress()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        data.UseLegacyLimitsSchema();
        data.FailAt = "Migration.BeforeCommit";
        await Assert.ThrowsAsync<IOException>(() => data.NewStore().InitializeAsync());
        Assert.Equal(1L, data.Scalar("PRAGMA user_version;"));
        Assert.Equal(5L, data.Scalar("SELECT adaptive_limit FROM daily_limits;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        data.FailAt = null;
        await data.NewStore().InitializeAsync();
        Assert.Equal(4L, data.Scalar("PRAGMA user_version;"));
        Assert.Equal(DBNull.Value, data.Scalar("SELECT adaptive_limit FROM daily_limits;"));
        Assert.Equal("5", data.Scalar("SELECT configured_limit FROM daily_limits;"));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }

    [Fact]
    public async Task OneSenseInMultipleTopicsSharesProgressAndZeroNewLimitStillAllowsDueCards()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: "旅行"))!;
        var result = await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5, category: "商業"));
        var due = (await data.Store.GetNextReviewAsync(result.DueAt, 0, category: "商業"))!;
        Assert.Equal(card.Word.Id, due.Word.Id);
        Assert.False(due.IsNew);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM cards;"));
    }

    [Fact]
    public async Task MigrationFailureRollsBackAndNewerDatabaseIsNeverOverwritten()
    {
        using var data = new StudyTestData();
        data.FailAt = "Migration.BeforeCommit";
        await Assert.ThrowsAsync<IOException>(() => data.Store.InitializeAsync());
        Assert.Equal(0L, data.Scalar("PRAGMA user_version;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table';"));
        data.FailAt = null;
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        data.Execute("PRAGMA user_version=99;");
        await Assert.ThrowsAsync<StudyDataException>(() => data.NewStore().InitializeAsync());
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM senses;"));
        Assert.Equal(99L, data.Scalar("PRAGMA user_version;"));
    }
}
