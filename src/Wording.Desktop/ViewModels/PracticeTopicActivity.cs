using Wording.Core;

namespace Wording.Desktop.ViewModels;

public sealed record PracticeTopicActivity(string Name, int ReadingCount, int ListeningCount, DateTimeOffset? LastCompletedAt)
{
    public int Count => ReadingCount + ListeningCount;
    public string CountLabel => $"{Count} 次";
    public string ModeLabel => $"閱讀 {ReadingCount} · 聽力 {ListeningCount}";
    public string LastPracticeLabel => LastCompletedAt is { } last ? $"最近 {last.ToLocalTime():MM/dd}" : "尚未練習";
    public string Description => $"{Name} · 最近三個月 {Count} 次 · {ModeLabel} · {LastPracticeLabel}";
    public string Color { get; init; } = "#F2F3F7";
    public string Foreground { get; init; } = "#66758C";

    public static PracticeTopicActivity[] Build(IEnumerable<string> availableTopics, IReadOnlyList<PracticeCompletion> history)
    {
        // One completion contributes once to each topic, even if input labels differ only by case.
        var byTopic = history.SelectMany(x => x.Topics.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(topic => (topic, completion: x)))
            .GroupBy(x => x.topic, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(y => y.completion).ToArray(), StringComparer.OrdinalIgnoreCase);
        var topics = availableTopics.Concat(byTopic.Keys).Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var result = topics.Select(topic =>
        {
            var completions = byTopic.GetValueOrDefault(topic) ?? [];
            return new PracticeTopicActivity(topic, completions.Count(x => x.Mode == PracticeMode.Reading),
                completions.Count(x => x.Mode == PracticeMode.Listening), completions.Length == 0 ? null : completions.Max(x => x.CompletedAt));
        }).ToArray();
        var maximum = result.Select(x => x.Count).DefaultIfEmpty().Max();
        return result.Select(x => x.Count == 0 ? x : x with
        {
            Color = ((double)x.Count / maximum) switch
            { <= 0.25 => "#EDE9FE", <= 0.5 => "#D8CEFB", <= 0.75 => "#AB97EC", _ => "#7562CB" },
            Foreground = (double)x.Count / maximum > 0.75 ? "#FFFFFF" : "#493879"
        }).ToArray();
    }
}
