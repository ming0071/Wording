using System.Text.Json;
using Microsoft.Data.Sqlite;
using Wording.Core;
using Wording.Infrastructure;

if (args.Length != 2 || args[0] is not ("seed" or "check")) throw new ArgumentException("seed/check and an isolated data directory are required.");
var directory = Path.GetFullPath(args[1]);
if (!directory.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Probe data must stay inside workspace artifacts.");
Directory.CreateDirectory(directory);
var store = new SqliteStudyStore(Path.Combine(directory, "wording.db"));
await store.InitializeAsync();
var snapshot = Path.Combine(directory, "expected-installer-data.json");
if (args[0] == "seed")
{
    if (File.Exists(snapshot)) throw new IOException("Refusing to overwrite existing probe data.");
    var word = new VocabularyItem { Headword = "private installation vocabulary", Meaning = "個人安裝驗證詞", PartOfSpeech = "noun",
        IsStarred = true, Categories = ["自己的分類"], Notes = "個人筆記必須保留。", Examples = [new("My own example stays here.", "個人例句保留。")], Enrollment = Enrollment.Selected };
    await store.SaveVocabularyAsync(word);
    var now = DateTimeOffset.UtcNow;
    var card = (await store.GetNextReviewAsync(now, 55))!;
    await store.SubmitReviewAsync(new(word.Id, ReviewRating.Easy, now, Guid.NewGuid(), card.ScheduleVersion));
    new AppSettings { DailyNewLimit = 55, AutoSpeakWord = false, AutoSpeakExamples = false }.Save(directory);
    File.WriteAllText(snapshot, Snapshot());
}
else if (File.ReadAllText(snapshot) != Snapshot()) throw new InvalidDataException("Installer changed personal vocabulary, schedule, history, or settings.");
Console.WriteLine("PASS: isolated vocabulary, progress and settings " + args[0]);

string Snapshot()
{
    using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Mode=ReadOnly;Pooling=False");
    connection.Open();
    var tables = new Dictionary<string, List<object?[]>>();
    foreach (var table in new[] { "senses", "cards", "review_log", "new_starts", "sense_categories" })
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray());
        tables[table] = rows;
    }
    var settings = AppSettings.Load(directory);
    return JsonSerializer.Serialize(new { tables, limit = settings.DailyNewLimit.ToString(), settings.AutoSpeakWord, settings.AutoSpeakExamples });
}
