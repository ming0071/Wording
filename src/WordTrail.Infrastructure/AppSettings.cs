using System.Text.Json;

namespace WordTrail.Infrastructure;

public sealed class AppSettings
{
    public int DailyNewLimit { get; set; } = 5;
    public string CodexExecutablePath { get; set; } = "codex";
    public string CodexModel { get; set; } = "";
    public int DailyGenerationLimit { get; set; } = 10;

    public static AppSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("設定檔格式無效；請先保留 settings.json，再恢復設定。");
    }

    public void Save(string dataDirectory)
    {
        if (DailyNewLimit is < 0 or > 10 || DailyGenerationLimit is < 0 or > 50)
            throw new ArgumentException("每日新詞須為 0–10，AI 次數須為 0–50。");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

public sealed class AiSettings
{
    public string ExecutablePath { get; set; } = "codex";
    public string Model { get; set; } = "";
    public int DailyGenerationLimit { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 120;
}
