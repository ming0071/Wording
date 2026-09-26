using System.Text.Json;
using WordTrail.Core;
using WordTrail.Infrastructure;
using Xunit;

namespace WordTrail.Tests;

public sealed class FsrsSchedulerTests
{
    [Fact]
    public void SchedulerMatchesOfficialPythonReferenceVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fsrs-reference.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal("6.3.1", document.RootElement.GetProperty("version").GetString());
        Assert.Equal(FsrsScheduler.GetParameters(), document.RootElement.GetProperty("parameters")
            .EnumerateArray().Select(x => x.GetDouble()).ToArray());
        var count = 0;
        foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var scenario = element.Deserialize<ReferenceCase>(options)!;
            var scheduler = new FsrsScheduler(scenario.MaximumInterval);
            var actual = scheduler.Review(scenario.Before, scenario.Rating, scenario.ReviewedAt);
            var expected = scenario.After;
            Assert.True(actual.State == expected.State, scenario.Name + ": state");
            Assert.True(actual.Step == expected.Step, scenario.Name + ": step");
            Assert.Equal(expected.CardId, actual.CardId);
            Assert.True(Math.Abs((actual.DueAt - expected.DueAt).TotalMilliseconds) <= 1, scenario.Name + ": due");
            Assert.Equal(expected.LastReviewAt, actual.LastReviewAt);
            AssertClose(expected.Stability, actual.Stability, scenario.Name + ": stability");
            AssertClose(expected.Difficulty, actual.Difficulty, scenario.Name + ": difficulty");
            count++;
        }
        Assert.Equal(58, count);
    }

    [Fact]
    public void SchedulerRejectsClockGoingBackwardsAndDoesNotMutateInput()
    {
        var scheduler = new FsrsScheduler();
        var time = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var first = scheduler.Create(Guid.NewGuid(), time);
        var reviewed = scheduler.Review(first, ReviewRating.Good, time);
        Assert.Null(first.LastReviewAt);
        Assert.Throws<ReviewConflictException>(() => scheduler.Review(reviewed, ReviewRating.Good, time.AddSeconds(-1)));
    }

    private static void AssertClose(double? expected, double? actual, string scenario)
    {
        if (expected is null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        var tolerance = Math.Max(1e-10, Math.Abs(expected.Value) * 1e-10);
        Assert.True(Math.Abs(expected.Value - actual.Value) <= tolerance,
            $"{scenario}: expected {expected:R}, actual {actual:R}");
    }

    private sealed record ReferenceCase(string Name, int MaximumInterval, ReviewRating Rating,
        DateTimeOffset ReviewedAt, ReviewSchedule Before, ReviewSchedule After);
}
