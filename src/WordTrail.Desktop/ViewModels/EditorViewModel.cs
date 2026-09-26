using System.Collections.ObjectModel;
using WordTrail.Core;

namespace WordTrail.Desktop.ViewModels;

public sealed class ExampleEditor : ObservableObject
{
    private string english = "";
    private string chinese = "";
    public string English { get => english; set => SetProperty(ref english, value); }
    public string Chinese { get => chinese; set => SetProperty(ref chinese, value); }
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
    private string cue;
    private string categoriesText;
    private string collocationsText;
    private string level;
    private bool selectedForStudy;
    private bool isArchived;
    private bool aiConsent;
    private AiEnrichment? preview;
    private ContentOrigin origin;

    public string Title { get; }
    public string Headword { get => headword; set => SetProperty(ref headword, value); }
    public string PartOfSpeech { get => partOfSpeech; set => SetProperty(ref partOfSpeech, value); }
    public string Meaning { get => meaning; set => SetProperty(ref meaning, value); }
    public string Cue { get => cue; set => SetProperty(ref cue, value); }
    public string CategoriesText { get => categoriesText; set => SetProperty(ref categoriesText, value); }
    public string CollocationsText { get => collocationsText; set => SetProperty(ref collocationsText, value); }
    public string Level { get => level; set => SetProperty(ref level, value); }
    public string[] Levels { get; } = ["基礎", "優先", "延伸"];
    public bool SelectedForStudy { get => selectedForStudy; set => SetProperty(ref selectedForStudy, value); }
    public bool IsArchived { get => isArchived; set => SetProperty(ref isArchived, value); }
    public bool AiConsent { get => aiConsent; set { SetProperty(ref aiConsent, value); GenerateCommand.NotifyCanExecuteChanged(); } }
    public ObservableCollection<ExampleEditor> Examples { get; } = [];
    public ObservableCollection<string> AvailableCategories { get; } = [];
    public string CategoryHint => AvailableCategories.Count == 0 ? "還沒有分類；輸入名稱即可在保存時建立。" : "現有分類：" + string.Join("、", AvailableCategories);
    public AiEnrichment? Preview { get => preview; private set { SetProperty(ref preview, value); OnPropertyChanged(nameof(HasPreview)); ApplyPreviewCommand.NotifyCanExecuteChanged(); } }
    public bool HasPreview => Preview is not null;
    public string OriginText => $"目前內容來源：{origin.Note}";
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand GenerateCommand { get; }
    public RelayCommand CancelGenerationCommand { get; }
    public RelayCommand ApplyPreviewCommand { get; }
    public RelayCommand DiscardPreviewCommand { get; }
    public RelayCommand AddExampleCommand { get; }
    public RelayCommand DictionaryCommand { get; }

    public EditorViewModel(IStudyStore store, IContentGenerator generator, VocabularyItem? item, Action<VocabularyItem> saved)
    {
        this.store = store;
        this.generator = generator;
        this.saved = saved;
        original = item ?? new VocabularyItem { Enrollment = Enrollment.Selected };
        Title = item is null ? "新增詞義" : "編輯詞義";
        headword = original.Headword;
        partOfSpeech = original.PartOfSpeech;
        meaning = original.Meaning;
        cue = original.Cue;
        categoriesText = string.Join("、", original.Categories);
        collocationsText = string.Join(Environment.NewLine, original.Collocations);
        level = original.Level;
        selectedForStudy = original.Enrollment == Enrollment.Selected;
        isArchived = original.IsArchived;
        origin = original.Origin;
        SetExamples(original.Examples);
        SaveCommand = Command(SaveAsync, () => !GenerateCommand.IsRunning);
        GenerateCommand = Command(GenerateAsync, () => AiConsent && !SaveCommand.IsRunning);
        GenerateCommand.PropertyChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
        SaveCommand.PropertyChanged += (_, _) => GenerateCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand = new(_ => GenerateCommand.Cancel());
        ApplyPreviewCommand = new(_ => ApplyPreview(), _ => Preview is not null && !GenerateCommand.IsRunning);
        DiscardPreviewCommand = new(_ => Preview = null);
        AddExampleCommand = new(_ => Examples.Add(new ExampleEditor()));
        DictionaryCommand = new(_ => UiActions.OpenDictionary(Headword, message => Error = message));
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
        return original with
        {
            Headword = Headword.Trim(), PartOfSpeech = PartOfSpeech.Trim(), Meaning = Meaning.Trim(), Cue = Cue.Trim(),
            Categories = categories, Collocations = CollocationsText.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Examples = examples.Select(example => new ExampleSentence(example.English.Trim(), example.Chinese.Trim())).ToArray(),
            Level = Level, Enrollment = SelectedForStudy ? Enrollment.Selected : original.Enrollment == Enrollment.Selected ? Enrollment.Candidate : original.Enrollment,
            Kind = Headword.Trim().Contains(' ') ? "phrase" : "word", IsArchived = IsArchived, IsUserEdited = true, Origin = origin
        };
    }

    private async Task SaveAsync(CancellationToken token)
    {
        var item = BuildItem();
        await store.SaveVocabularyAsync(item, token);
        Notice = "已保存。修改內容不會重設既有複習進度。";
        saved(item);
    }

    private async Task GenerateAsync(CancellationToken token)
    {
        var item = BuildItem();
        Preview = null;
        var result = await generator.GenerateAsync(item, token);
        token.ThrowIfCancellationRequested();
        Preview = result;
        Notice = "AI 內容已產生；先檢查，再按「套用到編輯欄位」。最後仍需按保存。";
    }

    private void ApplyPreview()
    {
        if (Preview is not { } result) return;
        Meaning = result.Meaning;
        CollocationsText = string.Join(Environment.NewLine, result.Collocations);
        SetExamples(result.Examples);
        origin = result.Origin;
        OnPropertyChanged(nameof(OriginText));
        Preview = null;
        Notice = "已套用到欄位，尚未保存。你可以調整內容後再保存。";
    }

    private void SetExamples(IEnumerable<ExampleSentence> examples)
    {
        Examples.Clear();
        foreach (var example in examples) Examples.Add(new ExampleEditor { English = example.English, Chinese = example.Chinese });
        if (Examples.Count == 0) Examples.Add(new ExampleEditor());
    }
}
