using Wording.Core;

namespace Wording.Infrastructure;

public sealed class TranslationSelector(Random? random = null)
{
    public const double FamiliarStabilityDays = 21;
    public const double MinimumRecall = 0.9;
    private readonly Random random = random ?? Random.Shared;

    public bool IsUnfamiliar(PracticeCandidate candidate, DateTimeOffset now) =>
        !candidate.Word.IsArchived && !candidate.Word.IsPaused && candidate.Word.Enrollment != Enrollment.Skipped &&
        (candidate.Word.IsStarred || candidate.Schedule.LastReviewAt is null ||
         candidate.Schedule.State != LearningState.Review || (candidate.Schedule.Stability ?? 0) < FamiliarStabilityDays ||
         Recall(candidate.Schedule, now) < MinimumRecall);

    public TranslationRequest Select(IReadOnlyList<PracticeCandidate> candidates, int count, DateTimeOffset now,
        IReadOnlyCollection<Guid>? recentWords = null)
    {
        if (count is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(count));
        var pool = candidates.Where(x => IsUnfamiliar(x, now)).DistinctBy(x => x.Word.WordId).ToList();
        if (pool.Count == 0)
            throw new InvalidOperationException("目前沒有可選的較不熟悉單字。可在單字庫標星想加強的詞彙，再回來出題。");
        var chosen = new List<VocabularyItem>();
        while (chosen.Count < count && pool.Count > 0)
        {
            // Exhaust other eligible words before repeating targets from the last two batches.
            var fresh = pool.Where(x => recentWords?.Contains(x.Word.WordId) != true).ToArray();
            var available = fresh.Length > 0 ? fresh : pool.ToArray();
            double Weight(PracticeCandidate candidate) =>
                (1 + 3 / (1 + (candidate.Schedule.Stability ?? 0) / 7) + 2 * (1 - Recall(candidate.Schedule, now))) *
                (candidate.Word.IsStarred ? 1.6 : 1);
            var roll = random.NextDouble() * available.Sum(Weight);
            var picked = available[^1];
            foreach (var candidate in available)
            {
                roll -= Weight(candidate);
                if (roll <= 0) { picked = candidate; break; }
            }
            var definitions = picked.Word.Definitions;
            chosen.Add(definitions[random.Next(definitions.Count)].AsWord(picked.Word.Headword));
            pool.Remove(picked);
        }
        return new(chosen.ToArray());
    }

    private static double Recall(ReviewSchedule schedule, DateTimeOffset now)
    {
        if (schedule.LastReviewAt is not { } last || schedule.Stability is not > 0) return 0;
        var decay = -FsrsScheduler.GetParameters()[20];
        var factor = Math.Pow(0.9, 1 / decay) - 1;
        return Math.Pow(1 + factor * Math.Max(0, (now - last).TotalDays) / schedule.Stability.Value, decay);
    }
}
