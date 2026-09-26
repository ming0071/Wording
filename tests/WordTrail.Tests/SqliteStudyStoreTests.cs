using WordTrail.Core;
using WordTrail.Infrastructure;
using Xunit;

namespace WordTrail.Tests;

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
        Assert.Equal(Enrollment.Candidate, imported.Enrollment);
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5));
        await data.Store.SetEnrollmentAsync(word.Id, Enrollment.Selected);
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
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 5));
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

    [Fact]
    public async Task DueCardsArePrioritizedAndAdaptiveQuotaIsPersisted()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        for (var index = 0; index < 12; index++) await data.AddWordAsync("word" + index);
        var now = data.Clock.Now.ToUnixTimeMilliseconds();
        // Fixture creates ten already learned/due cards; no fabricated review actions in production.
        data.Execute($"UPDATE cards SET last_review_ms={now - 86400000},due_ms={now},state=2,step=NULL,stability=2,difficulty=5 " +
            "WHERE sense_id IN (SELECT id FROM senses ORDER BY rowid LIMIT 10);");
        var due = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        Assert.False(due.IsNew);
        Assert.Equal(2L, data.Scalar("SELECT adaptive_limit FROM daily_limits;"));
        await data.Store.SubmitReviewAsync(new(due.Word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), due.ScheduleVersion));
        await data.NewStore().GetNextReviewAsync(data.Clock.Now, 5);
        Assert.Equal(2L, data.Scalar("SELECT adaptive_limit FROM daily_limits;"));
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
