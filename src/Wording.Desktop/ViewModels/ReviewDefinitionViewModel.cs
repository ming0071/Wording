using Wording.Core;

namespace Wording.Desktop.ViewModels;

public sealed class ReviewDefinitionViewModel(VocabularyDefinition definition)
{
    public string PartOfSpeech => definition.PartOfSpeech;
    public string Cue => definition.Cue;
    public string Meaning => definition.Meaning;
    public string EnglishDefinition => definition.EnglishDefinition;
    public string CollocationsText => string.Join(" · ", definition.Collocations);
    public string SynonymsText => string.Join("、", definition.Synonyms);
    public string Notes => definition.Notes;
    public IReadOnlyList<ExampleSentence> Examples => definition.Examples;
    public bool HasCue => !string.IsNullOrWhiteSpace(Cue);
    public bool HasEnglishDefinition => !string.IsNullOrWhiteSpace(EnglishDefinition);
    public bool HasCollocations => definition.Collocations.Length > 0;
    public bool HasSynonyms => definition.Synonyms.Length > 0;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
}
