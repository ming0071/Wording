using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Wording.Core;

namespace Wording.Infrastructure;

public sealed class AppSettings
{
    public string SkippedUpdateVersion { get; set; } = "";
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public Wording.Core.PracticeOptions Practice { get; set; } = new();
    [JsonConverter(typeof(NewWordLimitConverter))]
    public BigInteger DailyNewLimit { get; set; } = BigInteger.Parse(ApplicationConfiguration.Current.Review.DailyNewLimit, CultureInfo.InvariantCulture);
    public string FlipKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Flip;
    public string AgainKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Again;
    public string HardKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Hard;
    public string GoodKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Good;
    public string EasyKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Easy;
    public string SpeakKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.Speak;
    public string SpeakExampleKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.SpeakExample;
    public string RecordReadingKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.RecordReading;
    public string ReplayReadingKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.ReplayReading;
    public string NextReadingKey { get; set; } = ApplicationConfiguration.Current.Shortcuts.NextReading;
    public bool AutoSpeakWord { get; set; } = ApplicationConfiguration.Current.Speech.AutoSpeakWord;
    public bool AutoSpeakExamples { get; set; } = ApplicationConfiguration.Current.Speech.AutoSpeakExamples;
    public static IReadOnlyList<string> ShortcutKeys => ShortcutDefaults.AvailableKeys;
    public string CodexExecutablePath { get; set; } = ApplicationConfiguration.Current.Ai.ExecutablePath;
    public string CodexModel { get; set; } = ApplicationConfiguration.Current.Ai.Model;
    public string CodexServiceTier { get; set; } = ApplicationConfiguration.Current.Ai.ServiceTier;
    public int DailyGenerationLimit { get; set; } = ApplicationConfiguration.Current.Ai.DailyGenerationLimit;

    public static AppSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new InvalidDataException("設定檔格式無效；請先保留 settings.json，再恢復設定。");
        // Older custom key mappings may already use S or E; assign unused keys for new actions.
        using var document = JsonDocument.Parse(json);
        var used = new HashSet<string>([settings.FlipKey, settings.AgainKey, settings.HardKey, settings.GoodKey, settings.EasyKey]);
        if (document.RootElement.TryGetProperty(nameof(SpeakKey), out _)) used.Add(settings.SpeakKey);
        if (document.RootElement.TryGetProperty(nameof(SpeakExampleKey), out _)) used.Add(settings.SpeakExampleKey);
        if (!document.RootElement.TryGetProperty(nameof(SpeakKey), out _))
            settings.SpeakKey = used.Add(settings.SpeakKey) ? settings.SpeakKey : ShortcutKeys.First(x => !used.Contains(x));
        used.Add(settings.SpeakKey);
        if (!document.RootElement.TryGetProperty(nameof(SpeakExampleKey), out _))
            settings.SpeakExampleKey = used.Add(settings.SpeakExampleKey) ? settings.SpeakExampleKey : ShortcutKeys.First(x => !used.Contains(x));
        // Reading actions run only on the answer side, so they may share the question-side flip key.
        var answerKeys = new HashSet<string>([settings.AgainKey, settings.HardKey, settings.GoodKey, settings.EasyKey, settings.SpeakKey, settings.SpeakExampleKey]);
        if (document.RootElement.TryGetProperty(nameof(RecordReadingKey), out _)) answerKeys.Add(settings.RecordReadingKey);
        if (document.RootElement.TryGetProperty(nameof(ReplayReadingKey), out _)) answerKeys.Add(settings.ReplayReadingKey);
        if (!document.RootElement.TryGetProperty(nameof(RecordReadingKey), out _))
            settings.RecordReadingKey = answerKeys.Add(settings.RecordReadingKey) ? settings.RecordReadingKey : ShortcutKeys.First(x => !answerKeys.Contains(x));
        answerKeys.Add(settings.RecordReadingKey);
        if (!document.RootElement.TryGetProperty(nameof(ReplayReadingKey), out _))
            settings.ReplayReadingKey = answerKeys.Add(settings.ReplayReadingKey) ? settings.ReplayReadingKey : ShortcutKeys.First(x => !answerKeys.Contains(x));
        answerKeys.Add(settings.ReplayReadingKey);
        if (!document.RootElement.TryGetProperty(nameof(NextReadingKey), out _))
            settings.NextReadingKey = answerKeys.Add(settings.NextReadingKey) ? settings.NextReadingKey : ShortcutKeys.First(x => !answerKeys.Contains(x));
        settings.MigrateWindowSizeShortcut();
        settings.Validate();
        return settings;
    }

    private void MigrateWindowSizeShortcut()
    {
        // F is now a window shortcut. Preserve legacy settings by moving only conflicting mappings to unused keys.
        (Func<string> Get, Action<string> Set)[] mappings =
        [
            (() => FlipKey, value => FlipKey = value), (() => AgainKey, value => AgainKey = value),
            (() => HardKey, value => HardKey = value), (() => GoodKey, value => GoodKey = value),
            (() => EasyKey, value => EasyKey = value), (() => SpeakKey, value => SpeakKey = value),
            (() => SpeakExampleKey, value => SpeakExampleKey = value), (() => RecordReadingKey, value => RecordReadingKey = value),
            (() => ReplayReadingKey, value => ReplayReadingKey = value), (() => NextReadingKey, value => NextReadingKey = value)
        ];
        var used = mappings.Select(mapping => mapping.Get()).ToHashSet();
        foreach (var mapping in mappings.Where(mapping => mapping.Get() == ShortcutDefaults.WindowSizeKey))
        {
            var replacement = ShortcutKeys.First(key => !used.Contains(key));
            mapping.Set(replacement); used.Add(replacement);
        }
    }

    public void Save(string dataDirectory)
    {
        Validate();
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public void Validate()
    {
        if (!Wording.Core.CodexServiceTier.IsValidId(CodexServiceTier))
            throw new ArgumentException("Codex 加速模式識別碼無效。");
        Practice ??= new();
        Practice.Validate();
        if (DailyNewLimit < 0 || DailyGenerationLimit < 0 || DailyGenerationLimit > ApplicationConfiguration.Current.Ai.MaximumDailyGenerations)
            throw new ArgumentException($"每日新詞須為非負整數，AI 次數須為 0–{ApplicationConfiguration.Current.Ai.MaximumDailyGenerations}。");
        var keys = new[] { FlipKey, AgainKey, HardKey, GoodKey, EasyKey, SpeakKey, SpeakExampleKey };
        if (keys.Any(key => !ShortcutKeys.Contains(key)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("翻卡、評分與朗讀請選擇不同的快捷鍵。");
        ShortcutDefaults.ValidateReadingKeys(RecordReadingKey, ReplayReadingKey, NextReadingKey, keys.Skip(1));
    }
}

// Read earlier numeric settings too; decimal strings preserve arbitrarily large limits exactly.
public sealed class NewWordLimitConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() :
            reader.TokenType == JsonTokenType.Number ? Encoding.UTF8.GetString(reader.ValueSpan) : null;
        if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            throw new JsonException("每日新詞上限須為非負整數。");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class AiSettings
{
    public string ExecutablePath { get; set; } = ApplicationConfiguration.Current.Ai.ExecutablePath;
    public string Model { get; set; } = ApplicationConfiguration.Current.Ai.Model;
    public string ServiceTier { get; set; } = ApplicationConfiguration.Current.Ai.ServiceTier;
    public int DailyGenerationLimit { get; set; } = ApplicationConfiguration.Current.Ai.DailyGenerationLimit;
    public int TimeoutSeconds { get; set; } = ApplicationConfiguration.Current.Ai.TimeoutSeconds;
}
