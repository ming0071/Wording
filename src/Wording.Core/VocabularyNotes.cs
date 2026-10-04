using System.Text.RegularExpressions;

namespace Wording.Core;

public static class VocabularyNotes
{
    // Only remove the boilerplate added by the phrasal-verb pack. Ordinary URLs and personal notes remain.
    public const string PackDisclaimer = "中英文解釋與例句為原創學習內容；選用 TOEIC 職場情境，非官方頻率排名。";
    private static readonly Regex PackMetadata = new(Regex.Escape(PackDisclaimer) +
        @"(?:詞義參考：https?://(?:www\.merriam-webster\.com|dictionary\.cambridge\.org|www\.collinsdictionary\.com)/[^\s。；]*)?",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Clean(string notes) => PackMetadata.IsMatch(notes) ? PackMetadata.Replace(notes, "").Trim() : notes;

    public static VocabularyItem Clean(VocabularyItem word)
    {
        var notes = Clean(word.Notes);
        var definitions = word.AdditionalSenses.Select(definition =>
        {
            var cleaned = Clean(definition.Notes);
            return cleaned != definition.Notes ? definition with { Notes = cleaned } : definition;
        }).ToArray();
        return notes == word.Notes && definitions.SequenceEqual(word.AdditionalSenses)
            ? word : word with { Notes = notes, AdditionalSenses = definitions };
    }
}
