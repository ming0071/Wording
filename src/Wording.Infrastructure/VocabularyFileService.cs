using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed record VocabularyDocument(int Version, VocabularyEntry[] Items);

// Content only: ordinary edits preserve progress; adding another meaning resets that word's card.
public sealed record VocabularyEntry
{
    public Guid Id { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? WordId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsStarred { get; init; }
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
    public VocabularyDefinition[] AdditionalSenses { get; init; } = [];

    public static VocabularyEntry From(VocabularyItem item) => new()
    {
        Id = item.Id, WordId = item.WordId, IsStarred = item.IsStarred, Headword = item.Headword, PartOfSpeech = item.PartOfSpeech, Meaning = item.Meaning,
        EnglishDefinition = item.EnglishDefinition, Cue = item.Cue, Level = item.Level, Categories = item.Categories,
        Collocations = item.Collocations, Synonyms = item.Synonyms, Notes = item.Notes,
        Examples = item.Examples.Select(x => x with { Origin = null }).ToArray(),
        AdditionalSenses = item.AdditionalSenses.Select(x => x with
        { Examples = x.Examples.Select(e => e with { Origin = null }).ToArray() }).ToArray()
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
        var document = new VocabularyDocument(1, items.OrderBy(x => x.Headword, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CreationOrder).Select(VocabularyEntry.From).ToArray());
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
        var existingWords = existing.Values.GroupBy(x => x.WordId).ToDictionary(x => x.Key, x => x.First());
        var imported = new List<VocabularyItem>();
        var identifiers = new HashSet<Guid>();
        var starOverrides = new Dictionary<Guid, bool>();
        var origin = new ContentOrigin("user", "JSON 匯入");
        foreach (var entry in document.Items)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Headword) || string.IsNullOrWhiteSpace(entry.Meaning) || string.IsNullOrWhiteSpace(entry.PartOfSpeech))
                throw new InvalidDataException($"第 {imported.Count + 1} 筆需要 headword、partOfSpeech 與 meaning。");
            if (entry.AdditionalSenses is null || entry.AdditionalSenses.Any(x => x is null))
                throw new InvalidDataException($"第 {imported.Count + 1} 筆的 additionalSenses 需要有效的解釋陣列。");
            var id = entry.Id;
            if (id == Guid.Empty)
            {
                var identity = $"wording-import-v1\n{entry.Headword.Trim().ToLowerInvariant()}\n{entry.PartOfSpeech.Trim()}\n{entry.Meaning.Trim()}";
                id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
            }
            if (!identifiers.Add(id)) throw new InvalidDataException($"第 {imported.Count + 1} 筆的 ID 或詞義重複，請合併後再匯入。");
            if (entry.IsStarred is { } starred) starOverrides.Add(id, starred);
            var original = existing.GetValueOrDefault(id) ?? existingWords.GetValueOrDefault(WordIdentity.For(entry.Headword))
                ?? new VocabularyItem { Id = id, Enrollment = Enrollment.Selected, Origin = origin };
            // Legacy files may provide several sense IDs for one headword. Combine them below.
            imported.Add(original with
            {
                Id = id, Headword = entry.Headword, PartOfSpeech = entry.PartOfSpeech, Meaning = entry.Meaning,
                EnglishDefinition = entry.EnglishDefinition, Cue = entry.Cue, Level = entry.Level,
                Kind = entry.Headword.Trim().Contains(' ') ? "phrase" : "word", Categories = entry.Categories,
                Collocations = entry.Collocations!, Synonyms = entry.Synonyms, Notes = entry.Notes,
                Examples = entry.Examples?.Select(x => x is null ? null! : x with { Origin =
                    original.Examples.FirstOrDefault(old => old.English == x.English && old.Chinese == x.Chinese)?.Origin ?? origin }).ToArray()!,
                MeaningOrigin = entry.Meaning == original.Meaning ? original.MeaningOrigin : origin,
                CollocationsOrigin = entry.Collocations is not null && entry.Collocations.SequenceEqual(original.Collocations) ? original.CollocationsOrigin : origin,
                AdditionalSenses = entry.AdditionalSenses,
                IsUserEdited = true
            });
        }
        var combined = new List<VocabularyItem>();
        var combinedStars = new Dictionary<Guid, bool>();
        foreach (var group in imported.GroupBy(x => x.WordId))
        {
            var entries = group.ToArray();
            var original = entries.Select(x => existing.GetValueOrDefault(x.Id)).FirstOrDefault(x => x is not null)
                ?? existingWords.GetValueOrDefault(group.Key);
            var word = VocabularyMerge.Combine(entries);
            if (original is not null)
            {
                var supplied = word.Definitions.Select(VocabularyMerge.Key).ToHashSet();
                var suppliedIds = word.Definitions.Select(x => x.Id).ToHashSet();
                // A focused supplement adds meanings without dropping definitions absent from that file.
                var retained = original.Definitions.Where(x => !supplied.Contains(VocabularyMerge.Key(x)) && !suppliedIds.Contains(x.Id)).ToArray();
                var definitions = word.Definitions.Concat(retained).ToList();
                word = word with
                {
                    Id = original.Id, Enrollment = original.Enrollment, IsPaused = original.IsPaused,
                    IsArchived = original.IsArchived, IsStarred = original.IsStarred,
                    Categories = word.Categories.Concat(retained.SelectMany(x => x.Categories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                };
                // Keep the existing primary definition first when a supplement supplies only another meaning.
                var primary = definitions.FindIndex(x => x.Id == original.Id);
                if (primary > 0)
                {
                    var definition = definitions[primary];
                    definitions.RemoveAt(primary);
                    definitions.Insert(0, definition);
                }
                word = VocabularyMerge.WithDefinitions(word, definitions);
            }
            var explicitStars = entries.Where(x => starOverrides.ContainsKey(x.Id)).ToArray();
            if (explicitStars.Length > 0) combinedStars[word.Id] = explicitStars.Any(x => starOverrides[x.Id]);
            combined.Add(word);
        }
        // Validation and writes share a transaction; a bad entry leaves the entire library unchanged.
        await store.SaveVocabularyBatchAsync(combined, token, combinedStars);
        return combined.Count;
    }
}
