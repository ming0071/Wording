using FSRS.Core.Enums;
using FSRS.Core.Interfaces;
using FSRS.Core.Models;
using FSRS.Core.Services;
using Wording.Core;

namespace Wording.Infrastructure;

/// <summary>固定參數的 FSRS 邊界；畫面和 SQLite 不需要認識外部套件的型別。</summary>
public sealed class FsrsScheduler
{
    public const string ParameterVersion = "fsrs6-wording-v1-py6.3.1-whole-days";
    private readonly Scheduler _scheduler;

    // 與 tests/Fixtures/fsrs-reference.json 的 Python oracle 完全相同。
    public static double[] GetParameters() =>
    [
        0.2172, 1.1771, 3.2602, 16.1507, 7.0114, 0.57, 2.0966,
        0.0069, 1.5261, 0.112, 1.0178, 1.849, 0.1133, 0.3127,
        2.2934, 0.2191, 3.0004, 0.7536, 0.3332, 0.1437, 0.2
    ];

    public FsrsScheduler(int maximumInterval = 36500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInterval, 1);
        _scheduler = new Scheduler(desiredRetention: 0.9, parameters: GetParameters(),
            learningSteps: [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)],
            relearningSteps: [TimeSpan.FromMinutes(10)], maximumInterval: maximumInterval,
            enableFuzzing: false, retrievabilityCalculator: new WholeDaysRetrievability());
    }

    public ReviewSchedule Create(Guid cardId, DateTimeOffset now) =>
        new(cardId, LearningState.Learning, 0, null, null, now.ToUniversalTime(), null);

    public ReviewSchedule Review(ReviewSchedule previous, ReviewRating rating, DateTimeOffset now)
    {
        if (!Enum.IsDefined(rating)) throw new ArgumentOutOfRangeException(nameof(rating));
        if (previous.LastReviewAt is { } last && now < last)
            throw new ReviewConflictException("電腦時間早於上次複習時間；請校正時間後重試。");

        var card = new Card(previous.CardId, (State)previous.State, previous.Step,
            previous.Stability, previous.Difficulty, previous.DueAt.UtcDateTime,
            previous.LastReviewAt?.UtcDateTime);
        var (updated, _) = _scheduler.ReviewCard(card, (Rating)rating, now.UtcDateTime);
        return new(updated.CardId, (LearningState)updated.State, updated.Step,
            updated.Stability, updated.Difficulty,
            new DateTimeOffset(DateTime.SpecifyKind(updated.Due, DateTimeKind.Utc)),
            updated.LastReview is { } reviewed
                ? new DateTimeOffset(DateTime.SpecifyKind(reviewed, DateTimeKind.Utc)) : null);
    }

    // FSRS.Core 1.0.7 使用 TotalDays；官方 py-fsrs 6.3.1 使用完整經過天數。
    // 套件提供此擴充介面，因此只修正這個邊界，不複製或重寫整套排程。
    private sealed class WholeDaysRetrievability : IRetrievabilityCalculator
    {
        public double CalculateRetrievability(Card card, DateTime? currentDateTime = null,
            double[]? parameters = null)
        {
            if (card.LastReview is null || card.Stability is null) return 0;
            var weights = parameters ?? GetParameters();
            var elapsedDays = Math.Max(0, ((currentDateTime ?? DateTime.UtcNow) - card.LastReview.Value).Days);
            var decay = -weights[20];
            var factor = Math.Pow(0.9, 1 / decay) - 1;
            return Math.Pow(1 + factor * elapsedDays / card.Stability.Value, decay);
        }
    }
}
