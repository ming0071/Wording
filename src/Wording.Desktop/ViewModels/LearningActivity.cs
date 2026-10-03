using Wording.Core;
using System.Globalization;

namespace Wording.Desktop.ViewModels;

public sealed record ActivityCell(DateOnly Day, int Count, bool InRange)
{
    public string Unit { get; init; } = "個詞義";
    public string Description => $"{Day:yyyy/MM/dd} · 完成 {Count} {Unit}";
    public string Color => !InRange ? "Transparent" : Count switch
        { 0 => "#EDF0F5", < 5 => "#C3E7D0", < 10 => "#81CC9D", < 20 => "#3AA66A", _ => "#207448" };
}
public sealed record ActivityWeek(string MonthLabel, IReadOnlyList<ActivityCell> Days);
public sealed record LearningActivity(IReadOnlyList<ActivityWeek> Weeks, int Total, int ActiveDays, int Streak)
{
    public static LearningActivity Build(IReadOnlyList<StudyActivity> history, DateOnly today, DateOnly? start = null, string unit = "個詞義")
    {
        var from = start ?? today.AddDays(-364);
        var counts = history.Where(x => x.Day >= from && x.Day <= today).GroupBy(x => x.Day)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ReviewedCount));
        var firstSunday = from.AddDays(-(int)from.DayOfWeek);
        var weeks = new List<ActivityWeek>();
        for (var sunday = firstSunday; sunday <= today; sunday = sunday.AddDays(7))
        {
            var days = Enumerable.Range(0, 7).Select(offset => sunday.AddDays(offset))
                .Select(day => new ActivityCell(day, counts.GetValueOrDefault(day), day >= from && day <= today) { Unit = unit }).ToArray();
            var monthStart = days.FirstOrDefault(x => x.InRange && x.Day.Day == 1);
            var label = monthStart is not null ? monthStart.Day.ToString("MMM", CultureInfo.InvariantCulture)
                : weeks.Count == 0 ? from.ToString("MMM", CultureInfo.InvariantCulture) : "";
            weeks.Add(new(label, days));
        }
        // A short partial month at the start must not overlap the following month's label.
        if (weeks.Skip(1).Take(2).Any(x => x.MonthLabel.Length > 0)) weeks[0] = weeks[0] with { MonthLabel = "" };
        var streak = 0;
        var cursor = counts.GetValueOrDefault(today) > 0 ? today : today.AddDays(-1);
        while (cursor >= from && counts.GetValueOrDefault(cursor) > 0) { streak++; cursor = cursor.AddDays(-1); }
        return new(weeks, counts.Values.Sum(), counts.Count(x => x.Value > 0), streak);
    }
}
