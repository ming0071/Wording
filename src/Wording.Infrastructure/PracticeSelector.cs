using Wording.Core;

namespace Wording.Infrastructure;

public sealed class PracticeSelector(Random? random = null)
{
    private readonly Random random = random ?? Random.Shared;

    public PracticeRequest Select(PracticeOptions options, IReadOnlyList<PracticeCandidate> candidates,
        IReadOnlyList<PracticeCompletion> history, DateTimeOffset now, IReadOnlyCollection<Guid>? recentSessionWords = null)
    {
        options.Validate();
        var config = ApplicationConfiguration.Current.Practice;
        var eligible = candidates.Where(x => !x.Word.IsArchived && !x.Word.IsPaused && x.Word.Enrollment != Enrollment.Skipped).ToArray();
        var topics = options.Topics.Where(PracticeTopics.IsScenarioTopic).ToArray();
        var autoSelect = topics.Length == 0;
        if (autoSelect)
        {
            var available = eligible.SelectMany(x => x.Word.Categories).Where(PracticeTopics.IsScenarioTopic)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (available.Length > 0)
                topics = [Draw(available, topic => 1.0 / (1 + history.Where(x => x.CompletedAt > now.AddDays(-config.TopicLookbackDays))
                    .Count(x => x.Topics.Contains(topic, StringComparer.OrdinalIgnoreCase))))];
            else topics = ["自由情境"];
        }
        var pool = eligible.Where(x => x.Word.Categories.Any(c => topics.Contains(c, StringComparer.OrdinalIgnoreCase)) ||
            autoSelect && topics.SequenceEqual(new[] { "自由情境" })).ToArray();
        if (pool.Length == 0) throw new InvalidOperationException("這些主題沒有可用詞彙，請選其他主題或先新增單字。");
        var count = Math.Min(pool.Length, config.Lengths[options.Length].TargetWords + (int)options.Density * config.DensityWordIncrement);
        var exposures = history.SelectMany(x => x.TargetIds.Select(id => (Id: id, At: x.CompletedAt)))
            .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Max(y => y.At));
        var decay = -FsrsScheduler.GetParameters()[20];
        var factor = Math.Pow(0.9, 1 / decay) - 1;
        double Weight(PracticeCandidate candidate)
        {
            var schedule = candidate.Schedule;
            var elapsed = schedule.LastReviewAt is { } last ? Math.Max(0, (now - last).TotalDays) : 0;
            var stability = schedule.Stability ?? 0;
            var retrievability = stability > 0 ? Math.Pow(1 + factor * elapsed / stability, decay) : 0;
            var weight = schedule.LastReviewAt is null ? 1 : config.LearnedBaseWeight + config.StabilityWeight / (1 + stability / config.StabilityScaleDays) + config.RetrievabilityWeight * (1 - retrievability);
            if (candidate.Word.IsStarred) weight *= config.StarredWeight;
            if (exposures.TryGetValue(candidate.Word.Id, out var exposure))
                weight *= Math.Clamp((now - exposure).TotalDays / config.ExposureRecoveryDays, config.RecentExposureMinimumWeight, 1);
            if (recentSessionWords?.Contains(candidate.Word.Id) == true) weight *= config.RecentExposureMinimumWeight;
            return weight;
        }
        // Reserve space for both new and learned vocabulary when both are available.
        var chosen = new List<PracticeCandidate>();
        Take(pool.Where(x => x.Schedule.LastReviewAt is null), (count + 1) / 2);
        Take(pool.Where(x => x.Schedule.LastReviewAt is not null), count - chosen.Count);
        Take(pool, count - chosen.Count);
        void Take(IEnumerable<PracticeCandidate> source, int amount)
        {
            var remaining = source.Except(chosen).ToList();
            for (var i = 0; i < amount && remaining.Count > 0; i++)
            {
                var picked = Draw(remaining, Weight);
                chosen.Add(picked);
                remaining.Remove(picked);
            }
        }
        var kind = options.Kind == PassageKind.Random ? options.Mode == PracticeMode.Reading
            ? Draw(new[] { PassageKind.Email, PassageKind.Notice, PassageKind.Advertisement, PassageKind.Business, PassageKind.Story }, _ => 1)
            : Draw(new[] { PassageKind.Dialogue, PassageKind.Monologue }, _ => 1) : options.Kind;
        return new(options with { Topics = topics, Kind = kind }, chosen.Select(x => x.Word).ToArray());
    }

    private T Draw<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        var weights = items.Select(weight).ToArray();
        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < items.Count; i++) { roll -= weights[i]; if (roll <= 0) return items[i]; }
        return items[^1];
    }
}
