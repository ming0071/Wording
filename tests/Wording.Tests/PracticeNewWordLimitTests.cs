using Wording.Core;

namespace Wording.Tests;

public sealed class PracticeNewWordLimitTests
{
    [Theory]
    [InlineData(PracticeMode.Reading)]
    [InlineData(PracticeMode.Listening)]
    public async Task PracticeAddsFiveWordsAboveFiftyWhileRegularReviewStillHonorsLimit(PracticeMode mode)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var words = Enumerable.Range(0, 56).Select(i => StudyTestData.Word($"word{i}")).ToArray();
        await data.Store.SaveVocabularyBatchAsync(words);
        await data.Store.GetNextReviewAsync(data.Clock.Now, 50);
        foreach (var word in words.Take(50))
            await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        Assert.Equal(50, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 50));
        var extra = words.Skip(50).Take(5).ToArray();
        var exercise = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, mode, ["商業"], extra.Select(x => x.Id).ToArray());
        await data.Store.CompletePracticeAsync(exercise);
        Assert.All(await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 50), x => Assert.True(x.CanRate));
        foreach (var word in extra)
        {
            var rating = new ReviewSubmission(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0);
            var result = await data.Store.RatePracticeAsync(exercise.Id, rating, 50);
            Assert.Equal(result, await data.Store.RatePracticeAsync(exercise.Id, rating, 50));
        }
        var summary = await data.Store.GetDashboardAsync(data.Clock.Now);
        Assert.Equal(55, summary.StartedToday);
        Assert.Equal(55, summary.ReviewedToday);
        Assert.Equal(55L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal("50", data.Scalar("SELECT configured_limit FROM daily_limits;"));
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 50));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.SubmitReviewAsync(
            new(words[55].Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0)));
        data.Clock.Now = data.Clock.Now.AddMinutes(10);
        Assert.NotNull(await data.Store.GetNextReviewAsync(data.Clock.Now, 50));
    }

    [Fact]
    public async Task ZeroLimitStillAllowsPracticeStartsAndUndoDoesNotCountTheWordTwice()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        Assert.Null(await data.Store.GetNextReviewAsync(data.Clock.Now, 0));
        var exercise = new PracticeCompletion(Guid.NewGuid(), data.Clock.Now, PracticeMode.Reading, ["商業"], [word.Id]);
        await data.Store.CompletePracticeAsync(exercise);
        var state = Assert.Single(await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 0));
        Assert.True(state.CanRate);
        var rating = new ReviewSubmission(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), state.Version);
        await data.Store.RatePracticeAsync(exercise.Id, rating, 0);
        await data.Store.UndoReviewAsync(rating.OperationId);
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        state = Assert.Single(await data.Store.GetPracticeEligibilityAsync(exercise.Id, data.Clock.Now, 0));
        Assert.True(state.CanRate);
        await data.Store.RatePracticeAsync(exercise.Id, rating with { OperationId = Guid.NewGuid(), ExpectedScheduleVersion = state.Version }, 0);
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
    }
}
