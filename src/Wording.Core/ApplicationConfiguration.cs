using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wording.Core;

// Product defaults are separate from the user's saved settings and versioned data formats.
public sealed record ApplicationConfiguration
{
    public const string RelativePath = "Configuration/app-defaults.json";
    private static readonly Lazy<ApplicationConfiguration> Loaded = new(() =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RelativePath))));
    public static ApplicationConfiguration Current => Loaded.Value;
    public required ReviewDefaults Review { get; init; }
    public required ShortcutDefaults Shortcuts { get; init; }
    public required SpeechDefaults Speech { get; init; }
    public required AiDefaults Ai { get; init; }
    public required PracticeConfiguration Practice { get; init; }

    public static ApplicationConfiguration Parse(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            var value = JsonSerializer.Deserialize<ApplicationConfiguration>(json, options)
                ?? throw new ArgumentException("缺少設定內容。");
            value.Validate();
            return value;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new InvalidDataException($"{RelativePath} 格式或數值無效：{exception.Message}", exception); }
    }

    private void Validate()
    {
        if (Review is null || Shortcuts is null || Speech is null || Ai is null || Practice is null ||
            Practice.Defaults is null || Practice.Lengths is null)
            throw new ArgumentException("設定區塊不能為空。");
        if (!BigInteger.TryParse(Review.DailyNewLimit, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            string.IsNullOrWhiteSpace(Review.PreferredInputLanguage) ||
            CultureInfo.GetCultureInfo(Review.PreferredInputLanguage).Name != "zh-TW" ||
            Review.ReduceNewAtDueCount < 1 || Review.PauseNewAtDueCount <= Review.ReduceNewAtDueCount || Review.ReducedNewLimit < 0)
            throw new ArgumentException("複習預設無效。");
        var keys = new[] { Shortcuts.Flip, Shortcuts.Again, Shortcuts.Hard, Shortcuts.Good, Shortcuts.Easy, Shortcuts.Speak, Shortcuts.SpeakExample };
        if (keys.Any(key => !ShortcutDefaults.AvailableKeys.Contains(key)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("快捷鍵不能為空或重複。");
        if (string.IsNullOrWhiteSpace(Ai.ExecutablePath) || Ai.Model is null || !CodexServiceTier.IsValidId(Ai.ServiceTier) || Ai.MaximumDailyGenerations < 1 ||
            Ai.DailyGenerationLimit < 0 || Ai.DailyGenerationLimit > Ai.MaximumDailyGenerations ||
            Ai.MinimumTimeoutSeconds < 1 || Ai.MaximumTimeoutSeconds < Ai.MinimumTimeoutSeconds ||
            Ai.TimeoutSeconds < Ai.MinimumTimeoutSeconds || Ai.TimeoutSeconds > Ai.MaximumTimeoutSeconds ||
            Ai.PracticeTimeoutSeconds < 1 || Ai.AvailabilityTimeoutSeconds < 1 || Ai.ModelCatalogTimeoutSeconds < 1)
            throw new ArgumentException("AI 預設無效。");
        var d = Practice.Defaults;
        if (!Enum.IsDefined(d.Mode) || !Enum.IsDefined(d.Kind) || !Enum.IsDefined(d.Level) ||
            !Enum.IsDefined(d.Length) || !Enum.IsDefined(d.Density) || d.QuestionCount is < 3 or > 5 || d.SpeechRate is < -2 or > 2 ||
            d.Mode == PracticeMode.Reading && d.Kind is PassageKind.Dialogue or PassageKind.Monologue ||
            d.Mode == PracticeMode.Listening && d.Kind is not (PassageKind.Random or PassageKind.Dialogue or PassageKind.Monologue))
            throw new ArgumentException("情境練習預設無效。");
        var weights = new[] { Practice.ExposureRecoveryDays, Practice.StarredWeight, Practice.LearnedBaseWeight,
            Practice.StabilityWeight, Practice.StabilityScaleDays, Practice.RetrievabilityWeight, Practice.RecentExposureMinimumWeight };
        if (Practice.HistoryMonths is < 1 or > 12 || Practice.TopicLookbackDays < 1 || Practice.RecentSessionCount < 0 ||
            Practice.DensityWordIncrement is < 0 or > 9 || weights.Any(x => !double.IsFinite(x) || x <= 0) || Practice.RecentExposureMinimumWeight > 1 ||
            Practice.Lengths.Count != Enum.GetValues<PracticeLength>().Length ||
            Enum.GetValues<PracticeLength>().Any(length => !Practice.Lengths.TryGetValue(length, out var range) || range is null ||
                range.MinimumWords < 1 || range.MaximumWords < range.MinimumWords || range.TargetWords is < 1 or > 20 ||
                range.TargetWords + (int)WordDensity.High * Practice.DensityWordIncrement > 20))
            throw new ArgumentException("情境練習範圍或選字權重無效。");
    }
}

public sealed record ReviewDefaults
{
    public required string DailyNewLimit { get; init; }
    public required string PreferredInputLanguage { get; init; }
    public required int ReduceNewAtDueCount { get; init; }
    public required int ReducedNewLimit { get; init; }
    public required int PauseNewAtDueCount { get; init; }
    public int? AdaptiveNewLimit(int dueCount) => dueCount >= PauseNewAtDueCount ? 0 : dueCount >= ReduceNewAtDueCount ? ReducedNewLimit : null;
}
public sealed record ShortcutDefaults
{
    public static IReadOnlyList<string> AvailableKeys { get; } =
        new[] { "Space", "Enter" }.Concat(Enumerable.Range(0, 10).Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .Concat(Enumerable.Range('A', 26).Select(x => ((char)x).ToString()))
            .Concat(Enumerable.Range(1, 12).Select(x => $"F{x}")).ToArray();
    public required string Flip { get; init; }
    public required string Again { get; init; }
    public required string Hard { get; init; }
    public required string Good { get; init; }
    public required string Easy { get; init; }
    public required string Speak { get; init; }
    public required string SpeakExample { get; init; }
}
public sealed record SpeechDefaults
{
    public required bool AutoSpeakWord { get; init; }
    public required bool AutoSpeakExamples { get; init; }
}
public sealed record AiDefaults
{
    public required string ExecutablePath { get; init; }
    public required string Model { get; init; }
    public required string ServiceTier { get; init; }
    public required int DailyGenerationLimit { get; init; }
    public required int MaximumDailyGenerations { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required int MinimumTimeoutSeconds { get; init; }
    public required int MaximumTimeoutSeconds { get; init; }
    public required int PracticeTimeoutSeconds { get; init; }
    public required int AvailabilityTimeoutSeconds { get; init; }
    public required int ModelCatalogTimeoutSeconds { get; init; }
}
public sealed record PracticeDefaults
{
    public required PracticeMode Mode { get; init; }
    public required PassageKind Kind { get; init; }
    public required PracticeLevel Level { get; init; }
    public required PracticeLength Length { get; init; }
    public required WordDensity Density { get; init; }
    public required int QuestionCount { get; init; }
    public required int SpeechRate { get; init; }
}
public sealed record PracticeLengthConfiguration
{
    public required int MinimumWords { get; init; }
    public required int MaximumWords { get; init; }
    public required int TargetWords { get; init; }
}
public sealed record PracticeConfiguration
{
    public required PracticeDefaults Defaults { get; init; }
    public required int HistoryMonths { get; init; }
    public required int TopicLookbackDays { get; init; }
    public required int RecentSessionCount { get; init; }
    public required double ExposureRecoveryDays { get; init; }
    public required double RecentExposureMinimumWeight { get; init; }
    public required double StarredWeight { get; init; }
    public required double LearnedBaseWeight { get; init; }
    public required double StabilityWeight { get; init; }
    public required double StabilityScaleDays { get; init; }
    public required double RetrievabilityWeight { get; init; }
    public required int DensityWordIncrement { get; init; }
    public required Dictionary<PracticeLength, PracticeLengthConfiguration> Lengths { get; init; }
}
