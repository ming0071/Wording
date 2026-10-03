using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wording.Infrastructure;

public sealed class AppSettings
{
    public Wording.Core.PracticeOptions Practice { get; set; } = new();
    [JsonConverter(typeof(NewWordLimitConverter))]
    public BigInteger DailyNewLimit { get; set; } = 5;
    public string FlipKey { get; set; } = "Space";
    public string AgainKey { get; set; } = "1";
    public string HardKey { get; set; } = "2";
    public string GoodKey { get; set; } = "3";
    public string EasyKey { get; set; } = "4";
    public string SpeakKey { get; set; } = "S";
    public string SpeakExampleKey { get; set; } = "E";
    public bool AutoSpeakWord { get; set; } = true;
    public bool AutoSpeakExamples { get; set; } = true;
    public static IReadOnlyList<string> ShortcutKeys { get; } =
        new[] { "Space", "Enter" }.Concat(Enumerable.Range(0, 10).Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .Concat(Enumerable.Range('A', 26).Select(x => ((char)x).ToString()))
            .Concat(Enumerable.Range(1, 12).Select(x => $"F{x}")).ToArray();
    public string CodexExecutablePath { get; set; } = "codex";
    public string CodexModel { get; set; } = "";
    public int DailyGenerationLimit { get; set; } = 10;

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
            settings.SpeakKey = used.Add("S") ? "S" : ShortcutKeys.First(x => !used.Contains(x));
        used.Add(settings.SpeakKey);
        if (!document.RootElement.TryGetProperty(nameof(SpeakExampleKey), out _))
            settings.SpeakExampleKey = used.Add("E") ? "E" : ShortcutKeys.First(x => !used.Contains(x));
        settings.Validate();
        return settings;
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
        Practice ??= new();
        Practice.Validate();
        if (DailyNewLimit < 0 || DailyGenerationLimit is < 0 or > 50)
            throw new ArgumentException("每日新詞須為非負整數，AI 次數須為 0–50。");
        var keys = new[] { FlipKey, AgainKey, HardKey, GoodKey, EasyKey, SpeakKey, SpeakExampleKey };
        if (keys.Any(key => !ShortcutKeys.Contains(key)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("翻卡、評分與朗讀請選擇不同的快捷鍵。");
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
    public string ExecutablePath { get; set; } = "codex";
    public string Model { get; set; } = "";
    public int DailyGenerationLimit { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 120;
}
