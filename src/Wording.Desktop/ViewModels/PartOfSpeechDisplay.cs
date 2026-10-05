namespace Wording.Desktop.ViewModels;

public static class PartOfSpeechDisplay
{
    public static string Chinese(string value) => string.Join("、", value
        .Split([',', '，', '/', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Translate));

    private static string Translate(string value) => value.Replace(" ", "").TrimEnd('.').ToLowerInvariant() switch
    {
        "n" or "noun" => "名詞",
        "n[c]" or "countablenoun" => "可數名詞",
        "n[u]" or "uncountablenoun" => "不可數名詞",
        "v" or "verb" => "動詞",
        "vi" or "v.i" or "intransitiveverb" => "不及物動詞",
        "vt" or "v.t" or "transitiveverb" => "及物動詞",
        "adj" or "adjective" => "形容詞",
        "adv" or "adverb" => "副詞",
        "prep" or "preposition" => "介系詞",
        "conj" or "conjunction" => "連接詞",
        "aux" or "auxiliary" or "auxiliaryverb" => "助動詞",
        "int" or "interj" or "interjection" => "感嘆詞",
        "pron" or "pronoun" => "代名詞",
        "det" or "determiner" => "限定詞",
        "phrase" => "片語",
        "phrasalverb" => "動詞片語",
        _ => value
    };
}
