using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WordTrail.Core;

namespace WordTrail.Infrastructure;

public sealed record VocabularyDocument(int Version, VocabularyEntry[] Items);

// Content only: editing a JSON file must never reset scheduling or unarchive a card.
public sealed record VocabularyEntry
{
    public Guid Id { get; init; }
    public string Headword { get; init; } = "";
    public string PartOfSpeech { get; init; } = "";
    public string Meaning { get; init; } = "";
    public string EnglishDefinition { get; init; } = "";
    public string Cue { get; init; } = "";
    public string Level { get; init; } = "優先";
    public string[] Categories { get; init; } = [];
    public string[] Collocations { get; init; } = [];
    public string[] Synonyms { get; init; } = [];
    public string Notes { get; init; } = "";
    public ExampleSentence[] Examples { get; init; } = [];

    public static VocabularyEntry From(VocabularyItem item) => new()
    {
        Id = item.Id, Headword = item.Headword, PartOfSpeech = item.PartOfSpeech, Meaning = item.Meaning,
        EnglishDefinition = item.EnglishDefinition, Cue = item.Cue, Level = item.Level, Categories = item.Categories,
        Collocations = item.Collocations, Synonyms = item.Synonyms, Notes = item.Notes,
        Examples = item.Examples.Select(x => x with { Origin = null }).ToArray()
    };
}

public sealed class VocabularyFileService(IStudyStore store)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<int> ExportAsync(string path, CancellationToken token = default)
    {
        var items = await store.GetVocabularyAsync(cancellationToken: token, includeArchived: true);
        var document = new VocabularyDocument(1, items.Select(VocabularyEntry.From).ToArray());
        var destination = Path.GetFullPath(path);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false), token);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return items.Count;
    }

    public async Task<int> ImportAsync(string path, CancellationToken token = default)
    {
        var document = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException("無法讀取單字 JSON。");
        if (document.Version != 1 || document.Items is null)
            throw new InvalidDataException("單字 JSON 需要 version: 1 與 items 陣列。");
        var existing = (await store.GetVocabularyAsync(cancellationToken: token, includeArchived: true)).ToDictionary(x => x.Id);
        var imported = new List<VocabularyItem>();
        var identifiers = new HashSet<Guid>();
        var origin = new ContentOrigin("user", "JSON 匯入");
        foreach (var entry in document.Items)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Headword) || string.IsNullOrWhiteSpace(entry.Meaning) || string.IsNullOrWhiteSpace(entry.PartOfSpeech))
                throw new InvalidDataException($"第 {imported.Count + 1} 筆需要 headword、partOfSpeech 與 meaning。");
            var id = entry.Id;
            if (id == Guid.Empty)
            {
                var identity = $"wordtrail-import-v1\n{entry.Headword.Trim().ToLowerInvariant()}\n{entry.PartOfSpeech.Trim()}\n{entry.Meaning.Trim()}";
                id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
            }
            if (!identifiers.Add(id)) throw new InvalidDataException($"第 {imported.Count + 1} 筆的 ID 或詞義重複，請合併後再匯入。");
            var original = existing.GetValueOrDefault(id) ?? new VocabularyItem { Id = id, Enrollment = Enrollment.Selected, Origin = origin };
            imported.Add(original with
            {
                Headword = entry.Headword, PartOfSpeech = entry.PartOfSpeech, Meaning = entry.Meaning,
                EnglishDefinition = entry.EnglishDefinition, Cue = entry.Cue, Level = entry.Level,
                Kind = entry.Headword.Trim().Contains(' ') ? "phrase" : "word", Categories = entry.Categories,
                Collocations = entry.Collocations!, Synonyms = entry.Synonyms, Notes = entry.Notes,
                Examples = entry.Examples?.Select(x => x is null ? null! : x with { Origin =
                    original.Examples.FirstOrDefault(old => old.English == x.English && old.Chinese == x.Chinese)?.Origin ?? origin }).ToArray()!,
                MeaningOrigin = entry.Meaning == original.Meaning ? original.MeaningOrigin : origin,
                CollocationsOrigin = entry.Collocations is not null && entry.Collocations.SequenceEqual(original.Collocations) ? original.CollocationsOrigin : origin,
                IsUserEdited = true
            });
        }
        // Validation and writes share a transaction; a bad entry leaves the entire library unchanged.
        await store.SaveVocabularyBatchAsync(imported, token);
        return imported.Count;
    }
}
