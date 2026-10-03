using System.Collections.ObjectModel;
using Wording.Core;

namespace Wording.Desktop.ViewModels;

public sealed class ExampleEditor : ObservableObject
{
    private string english = "";
    private string chinese = "";
    public string English { get => english; set => SetProperty(ref english, value); }
    public string Chinese { get => chinese; set => SetProperty(ref chinese, value); }
    public string OriginalEnglish { get; init; } = "";
    public string OriginalChinese { get; init; } = "";
    public ContentOrigin? Origin { get; init; }
}

public sealed class SenseEditor : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    private string senseLabel = "另一組解釋";
    public string SenseLabel { get => senseLabel; set => SetProperty(ref senseLabel, value); }
    internal AiEnrichment? AppliedGeneration { get; set; }
    private string partOfSpeech = "";
    private string meaning = "";
    private string englishDefinition = "";
    private string collocationsText = "";
    private string cue = "";
    private string synonymsText = "";
    private string notes = "";
    public string EnglishDefinition { get => englishDefinition; set => SetProperty(ref englishDefinition, value); }
    public string CollocationsText { get => collocationsText; set => SetProperty(ref collocationsText, value); }
    public string PartOfSpeech { get => partOfSpeech; set => SetProperty(ref partOfSpeech, value); }
    public string Meaning { get => meaning; set => SetProperty(ref meaning, value); }
    public string Cue { get => cue; set => SetProperty(ref cue, value); }
    public string SynonymsText { get => synonymsText; set => SetProperty(ref synonymsText, value); }
    public string Notes { get => notes; set => SetProperty(ref notes, value); }
    public string English { get => Examples.FirstOrDefault()?.English ?? ""; set { EnsureExample(); Examples[0].English = value; } }
    public string Chinese { get => Examples.FirstOrDefault()?.Chinese ?? ""; set { EnsureExample(); Examples[0].Chinese = value; } }
    public ObservableCollection<ExampleEditor> Examples { get; } = [];
    public IReadOnlyList<PartOfSpeechChoice> PartOfSpeechChoices => PartOfSpeechChoice.For(PartOfSpeech);
    public RelayCommand AddExampleCommand { get; }
    public RelayCommand RemoveExampleCommand { get; }
    public SenseEditor()
    {
        AddExampleCommand = new(_ => AddExample(new("", "")));
        RemoveExampleCommand = new(value => { if (value is ExampleEditor example) Examples.Remove(example); });
        AddExample(new("", ""));
        Examples.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Examples));
    }
    public void SetExamples(IEnumerable<ExampleSentence> examples)
    {
        Examples.Clear();
        foreach (var example in examples) AddExample(example);
    }
    private void AddExample(ExampleSentence example)
    {
        var editor = new ExampleEditor { English = example.English, Chinese = example.Chinese,
            Origin = example.Origin, OriginalEnglish = example.English, OriginalChinese = example.Chinese };
        editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Examples));
        Examples.Add(editor);
    }
    private void EnsureExample() { if (Examples.Count == 0) AddExample(new("", "")); }
}

public sealed record PartOfSpeechChoice(string Value, string Label)
{
    private static readonly PartOfSpeechChoice[] Common = [new("名詞", "n"), new("可數名詞", "n[C]"), new("不可數名詞", "n[U]"),
        new("形容詞", "adj"), new("動詞", "v"), new("不及物動詞", "vi"), new("及物動詞", "vt"), new("副詞", "adv"),
        new("介系詞", "prep"), new("片語", "phrase"), new("連接詞", "conj"), new("助動詞", "aux"), new("感嘆詞", "int"), new("代名詞", "pron")];
    public static IReadOnlyList<PartOfSpeechChoice> For(string value) => string.IsNullOrWhiteSpace(value) || Common.Any(x => x.Value == value)
        ? Common : [new(value, value), .. Common];
}

public sealed class EditorViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private readonly IContentGenerator generator;
    private readonly VocabularyItem original;
    private readonly Action<VocabularyItem> saved;
    private string headword;
    private string partOfSpeech;
    private string meaning;
    private string englishDefinition;
    private string cue;
    private string categoriesText;
    private string collocationsText;
    private string synonymsText;
    private string notes;
    private string level;
    private bool isPaused;
    private bool isArchived;
    private bool aiConsent;
    private AiEnrichment? preview;
    private ContentOrigin origin;
    private bool isDirty;
    private long editRevision;
    private VocabularyItem? previewInput;
    private string? generatedMeaning;
    private string[]? generatedCollocations;
    private ContentOrigin? generatedOrigin;
    private object? aiTarget;
    private object? previewTarget;
    private long previewRevision;

    public string Title { get; }
    public string SenseLabel => "解釋 1";
    public EditorViewModel PrimarySense => this;
    public IReadOnlyList<PartOfSpeechChoice> PartOfSpeechChoices { get; }
    public ObservableCollection<object> AiTargets { get; } = [];
    public object? AiTarget { get => aiTarget; set => SetProperty(ref aiTarget, value); }
    public bool IsDirty { get => isDirty; private set => SetProperty(ref isDirty, value); }
    public string Headword { get => headword; set { if (SetProperty(ref headword, value)) MarkEdited(); } }
    public string PartOfSpeech { get => partOfSpeech; set { if (SetProperty(ref partOfSpeech, value)) MarkEdited(); } }
    public string Meaning { get => meaning; set { if (SetProperty(ref meaning, value)) MarkEdited(); } }
    public string EnglishDefinition { get => englishDefinition; set { if (SetProperty(ref englishDefinition, value)) MarkEdited(); } }
    public string Cue { get => cue; set { if (SetProperty(ref cue, value)) MarkEdited(); } }
    public string CategoriesText { get => categoriesText; set { if (SetProperty(ref categoriesText, value)) { MarkEdited(); OnPropertyChanged(nameof(SelectedCategories)); } } }
    private string categoryDraft = "";
    public string CategoryDraft { get => categoryDraft; set => SetProperty(ref categoryDraft, value); }
    public string[] SelectedCategories => CategoriesText.Split([',', '，', '、', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public RelayCommand AddCategorySelectionCommand { get; }
    public RelayCommand RemoveCategorySelectionCommand { get; }
    private Uri? dictionaryUri;
    public Uri? DictionaryUri { get => dictionaryUri; private set => SetProperty(ref dictionaryUri, value); }
    private string selectedDictionary = "Wiktionary 英英";
    public string[] Dictionaries { get; } = ["Wiktionary 英英", "Cambridge 英漢", "Oxford 英英"];
    public string SelectedDictionary { get => selectedDictionary; set => SetProperty(ref selectedDictionary, value); }
    private int helperTabIndex;
    public int HelperTabIndex { get => helperTabIndex; set => SetProperty(ref helperTabIndex, value); }
    public string CollocationsText { get => collocationsText; set { if (SetProperty(ref collocationsText, value)) MarkEdited(); } }
    public string SynonymsText { get => synonymsText; set { if (SetProperty(ref synonymsText, value)) MarkEdited(); } }
    public string Notes { get => notes; set { if (SetProperty(ref notes, value)) MarkEdited(); } }
    public IReadOnlyList<string> PartOfSpeechOptions { get; }
    public ObservableCollection<SenseEditor> AdditionalSenses { get; } = [];
    public string Level { get => level; set { if (SetProperty(ref level, value)) MarkEdited(); } }
    public string[] Levels { get; } = ["基礎", "優先", "延伸"];
    public bool IsPaused { get => isPaused; set { if (SetProperty(ref isPaused, value)) MarkEdited(); } }
    public bool IsArchived { get => isArchived; set { if (SetProperty(ref isArchived, value)) MarkEdited(); } }
    public bool AiConsent { get => aiConsent; set { SetProperty(ref aiConsent, value); GenerateCommand.NotifyCanExecuteChanged(); } }
    public ObservableCollection<ExampleEditor> Examples { get; } = [];
    public ObservableCollection<string> AvailableCategories { get; } = [];
    public string CategoryHint => AvailableCategories.Count == 0 ? "還沒有分類；輸入名稱即可在保存時建立。" : "現有分類：" + string.Join("、", AvailableCategories);
    public AiEnrichment? Preview { get => preview; private set { SetProperty(ref preview, value); OnPropertyChanged(nameof(HasPreview)); ApplyPreviewCommand.NotifyCanExecuteChanged(); } }
    public bool HasPreview => Preview is not null;
    public string OriginText => $"詞義初始來源：{origin.Note}";
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand GenerateCommand { get; }
    public RelayCommand CancelGenerationCommand { get; }
    public RelayCommand ApplyPreviewCommand { get; }
    public RelayCommand DiscardPreviewCommand { get; }
    public RelayCommand AddExampleCommand { get; }
    public RelayCommand RemoveExampleCommand { get; }
    public AsyncCommand SaveNextCommand { get; }
    public RelayCommand AddSenseCommand { get; }
    public RelayCommand RemoveSenseCommand { get; }
    public RelayCommand DictionaryCommand { get; }
    public RelayCommand BackCommand { get; }
    public EditorViewModel(IStudyStore store, IContentGenerator generator, VocabularyItem? item, Action<VocabularyItem> saved,
        Action? back = null, Action? next = null)
    {
        this.store = store;
        this.generator = generator;
        this.saved = saved;
        original = item ?? new VocabularyItem { Enrollment = Enrollment.Selected };
        Title = item is null ? "新增詞義" : "編輯詞義";
        headword = original.Headword;
        partOfSpeech = original.PartOfSpeech;
        meaning = original.Meaning;
        englishDefinition = original.EnglishDefinition;
        cue = original.Cue;
        categoriesText = string.Join("、", original.Categories);
        collocationsText = string.Join(Environment.NewLine, original.Collocations);
        synonymsText = string.Join("、", original.Synonyms);
        notes = original.Notes;
        string[] commonParts = ["名詞", "動詞", "形容詞", "副詞", "代名詞", "介系詞", "連接詞", "感嘆詞", "限定詞", "助動詞",
            "片語", "名詞片語", "動詞片語", "介系詞片語", "副詞片語"];
        PartOfSpeechOptions = string.IsNullOrWhiteSpace(partOfSpeech) || commonParts.Contains(partOfSpeech)
            ? commonParts : [partOfSpeech, .. commonParts];
        PartOfSpeechChoices = PartOfSpeechChoice.For(partOfSpeech);
        level = original.Level;
        isPaused = original.IsPaused || original.Enrollment == Enrollment.Skipped;
        isArchived = original.IsArchived;
        origin = original.Origin;
        SetExamples(original.Examples);
        Examples.CollectionChanged += (_, _) => MarkEdited();
        SaveCommand = Command(SaveAsync, () => GenerateCommand?.IsRunning != true);
        SaveNextCommand = Command(async token => { await SaveAsync(token); if (!IsDirty) next?.Invoke(); }, () => !IsBusy && next is not null);
        GenerateCommand = Command(GenerateAsync, () => AiConsent && !SaveCommand.IsRunning);
        GenerateCommand.PropertyChanged += (_, _) => { SaveCommand.NotifyCanExecuteChanged(); ApplyPreviewCommand?.NotifyCanExecuteChanged(); };
        SaveCommand.PropertyChanged += (_, _) =>
        {
            GenerateCommand.NotifyCanExecuteChanged();
            AddSenseCommand?.NotifyCanExecuteChanged();
            RemoveSenseCommand?.NotifyCanExecuteChanged();
        };
        CancelGenerationCommand = new(_ => GenerateCommand.Cancel());
        ApplyPreviewCommand = new(_ => ApplyPreview(), _ => Preview is not null && !GenerateCommand.IsRunning);
        DiscardPreviewCommand = new(_ => Preview = null);
        AddExampleCommand = new(_ => AddExample(new ExampleSentence("", "")));
        RemoveExampleCommand = new(value => { if (value is ExampleEditor example) Examples.Remove(example); });
        AiTargets.Add(this);
        AiTarget = this;
        AddSenseCommand = new(_ =>
        {
            var sense = new SenseEditor { PartOfSpeech = PartOfSpeech, SenseLabel = $"解釋 {AdditionalSenses.Count + 2}" };
            sense.PropertyChanged += OnSenseEdited;
            AdditionalSenses.Add(sense);
            AiTargets.Add(sense);
            AiTarget = sense;
        }, _ => !IsBusy);
        RemoveSenseCommand = new(value =>
        {
            if (value is not SenseEditor sense) return;
            sense.PropertyChanged -= OnSenseEdited;
            AdditionalSenses.Remove(sense);
            AiTargets.Remove(sense);
            if (ReferenceEquals(AiTarget, sense)) AiTarget = this;
            for (var i = 0; i < AdditionalSenses.Count; i++) AdditionalSenses[i].SenseLabel = $"解釋 {i + 2}";
        }, _ => !IsBusy);
        AdditionalSenses.CollectionChanged += (_, _) => MarkEdited();
        DictionaryCommand = new(_ =>
        {
            if (string.IsNullOrWhiteSpace(Headword)) { Error = "請先輸入單字或片語。"; return; }
            var word = Uri.EscapeDataString(Headword.Trim());
            DictionaryUri = new Uri(SelectedDictionary switch
            {
                "Cambridge 英漢" => "https://dictionary.cambridge.org/dictionary/english-chinese-traditional/" + Uri.EscapeDataString(Headword.Trim().Replace(' ', '-')),
                "Oxford 英英" => "https://www.oxfordlearnersdictionaries.com/search/english/?q=" + word,
                _ => "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(Headword.Trim().Replace(' ', '_')) + "#English"
            });
            HelperTabIndex = 0;
            Error = "";
        });
        AddCategorySelectionCommand = new(_ =>
        {
            var name = CategoryDraft.Trim();
            if (name.Length is < 1 or > 80 || name.IndexOfAny([',', '，', '、', '\n']) >= 0)
            { Error = "請選擇主題，或輸入 1–80 字的新主題名稱。"; return; }
            CategoriesText = string.Join("、", SelectedCategories.Append(name).Distinct(StringComparer.OrdinalIgnoreCase));
            if (!AvailableCategories.Contains(name)) AvailableCategories.Add(name);
            CategoryDraft = "";
            Error = "";
        });
        RemoveCategorySelectionCommand = new(value => CategoriesText = string.Join("、", SelectedCategories.Where(x => x != value as string)));
        BackCommand = new(_ => back?.Invoke(), _ => !IsBusy);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IsBusy))
            {
                BackCommand.NotifyCanExecuteChanged(); SaveNextCommand.NotifyCanExecuteChanged();
                AddSenseCommand.NotifyCanExecuteChanged(); RemoveSenseCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var categories = await store.GetCategoriesAsync(cancellationToken);
        AvailableCategories.Clear();
        foreach (var category in categories) AvailableCategories.Add(category);
        OnPropertyChanged(nameof(CategoryHint));
    }

    private VocabularyItem BuildItem()
    {
        if (string.IsNullOrWhiteSpace(Headword) || string.IsNullOrWhiteSpace(Meaning) || string.IsNullOrWhiteSpace(PartOfSpeech))
            throw new InvalidOperationException("請填入單字／片語、詞性與這個詞義的繁中解釋。");
        if (Headword.Trim().Length > 100 || Meaning.Length > 1500)
            throw new InvalidOperationException("詞條最長 100 字，詞義最長 1500 字。");
        var examples = Examples.Where(example => !string.IsNullOrWhiteSpace(example.English) || !string.IsNullOrWhiteSpace(example.Chinese)).ToArray();
        if (examples.Any(example => string.IsNullOrWhiteSpace(example.English) || string.IsNullOrWhiteSpace(example.Chinese)))
            throw new InvalidOperationException("每個例句請同時填寫英文與繁中翻譯，或將兩欄留空。");
        var categories = CategoriesText.Split([',', '，', '、', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var collocations = CollocationsText.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var manualOrigin = new ContentOrigin("user", "使用者編輯");
        var meaningOrigin = generatedOrigin is not null && Meaning.Trim() == generatedMeaning
            ? generatedOrigin : Meaning.Trim() == original.Meaning ? original.MeaningOrigin ?? original.Origin : manualOrigin;
        var collocationsOrigin = generatedOrigin is not null && collocations.SequenceEqual(generatedCollocations ?? [])
            ? generatedOrigin : collocations.SequenceEqual(original.Collocations) ? original.CollocationsOrigin ?? original.Origin : manualOrigin;
        return original with
        {
            Headword = Headword.Trim(), PartOfSpeech = PartOfSpeech.Trim(), Meaning = Meaning.Trim(), Cue = Cue.Trim(),
            EnglishDefinition = EnglishDefinition.Trim(),
            Categories = categories, Collocations = collocations, Synonyms = SplitSynonyms(SynonymsText), Notes = Notes.Trim(),
            Examples = examples.Select(example => new ExampleSentence(example.English.Trim(), example.Chinese.Trim(),
                example.English.Trim() == example.OriginalEnglish && example.Chinese.Trim() == example.OriginalChinese
                    ? example.Origin ?? original.Origin : manualOrigin)).ToArray(),
            MeaningOrigin = meaningOrigin, CollocationsOrigin = collocationsOrigin,
            Level = Level, Enrollment = Enrollment.Selected, IsPaused = IsPaused,
            Kind = Headword.Trim().Contains(' ') ? "phrase" : "word", IsArchived = IsArchived, IsUserEdited = true, Origin = origin
        };
    }

    private async Task SaveAsync(CancellationToken token)
    {
        var item = BuildItem();
        var additional = AdditionalSenses.Select(sense => BuildAdditionalSense(sense, item)).ToArray();
        var savedRevision = editRevision;
        if (additional.Length == 0) await store.SaveVocabularyAsync(item, token);
        else await store.SaveVocabularyBatchAsync([item, .. additional], token);
        // 寫入期間仍可編輯；舊快照成功不能抹掉後來的修改，也不能通知外層離開編輯頁。
        if (editRevision != savedRevision)
        {
            Notice = "已保存按下按鈕時的內容；保存期間新增的修改尚未保存，請再按一次「保存詞義」。";
            return;
        }
        IsDirty = false;
        Notice = additional.Length == 0 ? "已保存。修改內容不會重設既有複習進度。" : $"已保存 {additional.Length + 1} 個詞義，每個詞義各自複習。";
        saved(item);
    }

    private static string[] SplitSynonyms(string text) => text.Split([',', '，', '、', '\r', '\n'],
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static VocabularyItem BuildAdditionalSense(SenseEditor sense, VocabularyItem common)
    {
        if (string.IsNullOrWhiteSpace(sense.Meaning) || string.IsNullOrWhiteSpace(sense.PartOfSpeech))
            throw new InvalidOperationException("新增的每個詞義都需要詞性與繁中解釋；不需要的欄位可按「移除詞義」。");
        if (sense.Meaning.Length > 1500)
            throw new InvalidOperationException("每個詞義的解釋最長 1500 字。");
        var examples = sense.Examples.Where(x => !string.IsNullOrWhiteSpace(x.English) || !string.IsNullOrWhiteSpace(x.Chinese)).ToArray();
        if (examples.Any(x => string.IsNullOrWhiteSpace(x.English) || string.IsNullOrWhiteSpace(x.Chinese)))
            throw new InvalidOperationException("新增詞義的例句請同時填寫英文與繁中翻譯。");
        return new VocabularyItem
        {
            Id = sense.Id, Headword = common.Headword, PartOfSpeech = sense.PartOfSpeech.Trim(), Meaning = sense.Meaning.Trim(),
            EnglishDefinition = sense.EnglishDefinition.Trim(),
            Collocations = sense.CollocationsText.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Cue = sense.Cue.Trim(), Categories = common.Categories, Level = common.Level, Kind = common.Kind,
            Enrollment = Enrollment.Selected, IsPaused = common.IsPaused, IsArchived = common.IsArchived,
            Synonyms = SplitSynonyms(sense.SynonymsText), Notes = sense.Notes.Trim(),
            MeaningOrigin = sense.AppliedGeneration is { } generated && generated.Meaning == sense.Meaning.Trim() ? generated.Origin : null,
            CollocationsOrigin = sense.AppliedGeneration is { } generatedCollocations &&
                string.Join(Environment.NewLine, generatedCollocations.Collocations) == sense.CollocationsText ? generatedCollocations.Origin : null,
            Examples = examples.Select(x => new ExampleSentence(x.English.Trim(), x.Chinese.Trim(),
                x.English.Trim() == x.OriginalEnglish && x.Chinese.Trim() == x.OriginalChinese ? x.Origin : new("user", "使用者編輯"))).ToArray()
        };
    }

    private void OnSenseEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => MarkEdited();

    private async Task GenerateAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Headword) || Headword.Trim().Length > 100)
            throw new InvalidOperationException("請先輸入 1–100 字的單字或片語。");
        var target = AiTarget;
        var item = target is SenseEditor sense
            ? new VocabularyItem { Id = sense.Id, Headword = Headword.Trim(), PartOfSpeech = sense.PartOfSpeech.Trim(), Meaning = sense.Meaning.Trim(), Cue = sense.Cue.Trim() }
            : original with { Headword = Headword.Trim(), PartOfSpeech = PartOfSpeech.Trim(), Meaning = Meaning.Trim(), Cue = Cue.Trim() };
        var revision = editRevision;
        Preview = null;
        var result = await generator.GenerateAsync(item, token);
        token.ThrowIfCancellationRequested();
        previewInput = item;
        previewTarget = target;
        previewRevision = revision;
        Preview = result;
        Notice = "AI 內容已產生；先檢查，再按「套用到編輯欄位」。最後仍需按保存。";
    }

    private void ApplyPreview()
    {
        if (Preview is not { } result) return;
        if (previewInput is null || previewRevision != editRevision || (previewTarget is SenseEditor removed && !AdditionalSenses.Contains(removed)))
        {
            Error = "詞條或指定詞義已變更，請重新生成，避免套用到不同詞義。";
            Preview = null;
            return;
        }
        if (previewTarget is SenseEditor target)
        {
            target.AppliedGeneration = result;
            target.Meaning = result.Meaning;
            target.EnglishDefinition = result.EnglishDefinition;
            target.CollocationsText = string.Join(Environment.NewLine, result.Collocations);
            if (result.Synonyms.Length > 0) target.SynonymsText = string.Join("、", result.Synonyms);
            target.SetExamples(result.Examples.Select(x => x with { Origin = result.Origin }));
            Preview = null;
            Notice = $"已填入{target.SenseLabel}，尚未保存。";
            return;
        }
        Meaning = result.Meaning;
        EnglishDefinition = result.EnglishDefinition;
        if (result.Synonyms.Length > 0) SynonymsText = string.Join("、", result.Synonyms);
        CollocationsText = string.Join(Environment.NewLine, result.Collocations);
        SetExamples(result.Examples.Select(example => example with { Origin = result.Origin }));
        generatedMeaning = result.Meaning;
        generatedCollocations = result.Collocations;
        generatedOrigin = result.Origin;
        MarkEdited();
        OnPropertyChanged(nameof(OriginText));
        Preview = null;
        Notice = "已套用到欄位，尚未保存。你可以調整內容後再保存。";
    }

    private void SetExamples(IEnumerable<ExampleSentence> examples)
    {
        Examples.Clear();
        foreach (var example in examples) AddExample(example);
        if (Examples.Count == 0) AddExample(new ExampleSentence("", ""));
    }

    private void AddExample(ExampleSentence example)
    {
        var editor = new ExampleEditor { English = example.English, Chinese = example.Chinese,
            OriginalEnglish = example.English, OriginalChinese = example.Chinese, Origin = example.Origin };
        editor.PropertyChanged += (_, _) => MarkEdited();
        Examples.Add(editor);
    }

    private void MarkEdited()
    {
        editRevision++;
        IsDirty = true;
    }
}
