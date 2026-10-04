namespace Wording.Core;

public static class VocabularyMerge
{
    public static VocabularyItem Combine(IReadOnlyList<VocabularyItem> words)
    {
        var first = words[0];
        var definitions = new List<VocabularyDefinition>();
        foreach (var definition in words.SelectMany(x => x.Definitions))
        {
            var index = definitions.FindIndex(x => Key(x) == Key(definition));
            if (index < 0) definitions.Add(definition);
            else
            {
                var previous = definitions[index];
                definitions[index] = previous with
                {
                    Categories = previous.Categories.Union(definition.Categories, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Collocations = previous.Collocations.Union(definition.Collocations, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Synonyms = previous.Synonyms.Union(definition.Synonyms, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Examples = previous.Examples.Concat(definition.Examples).DistinctBy(x => (x.English, x.Chinese)).ToArray(),
                    Notes = string.Join(Environment.NewLine, new[] { previous.Notes, definition.Notes }.Where(x => x.Length > 0).Distinct())
                };
            }
        }
        var primary = definitions[0];
        return first with
        {
            PartOfSpeech = primary.PartOfSpeech, Meaning = primary.Meaning, EnglishDefinition = primary.EnglishDefinition,
            Cue = primary.Cue, Collocations = primary.Collocations, Synonyms = primary.Synonyms,
            Notes = primary.Notes, Examples = primary.Examples,
            Categories = words.SelectMany(x => x.Categories).Concat(definitions.SelectMany(x => x.Categories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            IsStarred = words.Any(x => x.IsStarred), AdditionalSenses = definitions.Skip(1).ToArray()
        };
    }

    public static string Key(VocabularyDefinition definition)
    {
        var part = definition.PartOfSpeech.Trim().ToLowerInvariant() switch
        {
            "名詞" or "noun" => "n", "動詞" or "verb" => "v", "形容詞" or "adjective" => "adj",
            "副詞" or "adverb" => "adv", var value => value
        };
        return part + "\n" + string.Concat(definition.Meaning.Where(x => !char.IsWhiteSpace(x) && !char.IsPunctuation(x))).ToLowerInvariant();
    }

    public static VocabularyItem WithDefinitions(VocabularyItem word, IReadOnlyList<VocabularyDefinition> definitions)
    {
        var first = definitions[0];
        return word with
        {
            PartOfSpeech = first.PartOfSpeech, Meaning = first.Meaning, EnglishDefinition = first.EnglishDefinition,
            Cue = first.Cue, Level = first.Level, Notes = first.Notes, Examples = first.Examples,
            Collocations = first.Collocations, Synonyms = first.Synonyms,
            AdditionalSenses = definitions.Skip(1).ToArray()
        };
    }
}
