using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData(20, 3, 3, "每日上限 3 個")]
    [InlineData(10, 3, 3, "每日上限 3 個")]
    [InlineData(0, 0, 0, "設定中的每日新詞上限為 0")]
    [InlineData(0, 2, 2, "每日上限 2 個")]
    [InlineData(10, 1, 1, "每日上限 1 個")]
    public async Task EmptyReviewExplainsTheActualDailyQuota(int dueCount, int configuredLimit,
        int startedCount, string expectedReason)
    {
        using var data = new StudyTestData();
        data.Clock.Now = DateTimeOffset.UtcNow;
        await data.Store.InitializeAsync();
        for (var index = 0; index < dueCount + 4; index++)
            await data.AddWordAsync("word" + index);
        var now = data.Clock.Now.ToUnixTimeMilliseconds();
        data.Execute($"UPDATE cards SET last_review_ms={now - 86400000},due_ms={now - 1000}," +
            "state=2,step=NULL,stability=2,difficulty=5 WHERE sense_id IN " +
            $"(SELECT id FROM senses ORDER BY rowid LIMIT {dueCount});");

        // Initialize today's configured quota, then remove due cards from this fixture.
        await data.Store.GetNextReviewAsync(data.Clock.Now, configuredLimit);
        data.Execute($"UPDATE cards SET due_ms={now + 86400000} WHERE last_review_ms IS NOT NULL;");
        for (var index = 0; index < startedCount; index++)
        {
            var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, configuredLimit))!;
            await data.Store.SubmitReviewAsync(new(card.Word.Id, ReviewRating.Easy, data.Clock.Now,
                Guid.NewGuid(), card.ScheduleVersion));
        }

        var summary = await data.NewStore().GetDashboardAsync(data.Clock.Now);
        Assert.Equal(startedCount, summary.StartedToday);
        var review = new ReviewViewModel(data.NewStore(), new SilentSpeech(),
            new AppSettings { DailyNewLimit = configuredLimit, AutoSpeakWord = false });
        await review.LoadAsync();

        Assert.False(review.HasCard);
        Assert.Contains(expectedReason, review.EmptyText);
        Assert.Contains($"{startedCount} 個", review.EmptyText);
        Assert.DoesNotContain("或因到期複習量", review.EmptyText);
        Assert.DoesNotContain("減量", review.EmptyText);
        Assert.DoesNotContain("暫停提供新詞", review.EmptyText);
        Assert.Equal(0, (await data.Store.GetDashboardAsync(data.Clock.Now.AddDays(1))).StartedToday);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM daily_limits;"));
    }
}
