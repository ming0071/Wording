using System.Collections.ObjectModel;
using Wording.Core;

namespace Wording.Desktop.ViewModels;

public sealed class LibraryViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private readonly Action<VocabularyItem?> edit;
    private string searchText = "";
    private string selectedFilter = "全部";
    private string newCategory = "";
    private VocabularyItem? selectedItem;

    public ObservableCollection<VocabularyItem> Items { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public string[] Filters { get; } = ["全部", "學習中", "已暫停", "已封存"];
    public string[] SortOptions { get; } = ["建立時間：最新優先", "建立時間：最早優先", "單字：A–Z", "單字：Z–A", "熟練程度：低到高", "熟練程度：高到低", "星號優先"];
    private string selectedSort = "建立時間：最新優先";
    public string SelectedSort { get => selectedSort; set { if (SetProperty(ref selectedSort, value)) SearchCommand.Execute(null); } }
    public AsyncCommand StarCommand { get; }
    public RelayCommand StarRowCommand { get; }
    public string SearchText { get => searchText; set => SetProperty(ref searchText, value); }
    public CategorySelection CategoryScope { get; } = new();
    public bool CanChangeCategory => !IsBusy;
    public string SelectedCategory { get => CategoryScope.SelectedNames.FirstOrDefault() ?? "全部分類";
        set => CategoryScope.SetSelected(value == "全部分類" ? [] : [value]); }
    public string SelectedFilter { get => selectedFilter; set => SetProperty(ref selectedFilter, value); }
    public string NewCategory { get => newCategory; set => SetProperty(ref newCategory, value); }
    public string CountText => $"{Items.Count} 個單字／片語 · {Items.Sum(x => x.Definitions.Count)} 組解釋";
    public string PauseLabel => SelectedItem is { } item && (item.IsPaused || item.Enrollment == Enrollment.Skipped)
        ? "恢復複習" : "暫停複習";
    public VocabularyItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            SetProperty(ref selectedItem, value);
            OnPropertyChanged(nameof(PauseLabel));
            foreach (var command in SelectionCommands) command.NotifyCanExecuteChanged();
            EditCommand.NotifyCanExecuteChanged();
        }
    }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ArchiveCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    private AsyncCommand[] SelectionCommands => [PauseCommand, ArchiveCommand, RestoreCommand, StarCommand];

    public LibraryViewModel(IStudyStore store, Action<VocabularyItem?> edit)
    {
        this.store = store;
        this.edit = edit;
        SearchCommand = Command(LoadAsync);
        StarCommand = Command(async token =>
        {
            if (SelectedItem is not { } item) return;
            await store.SetStarredAsync(item.Id, !item.IsStarred, token);
            await LoadAsync(token);
        }, () => SelectedItem is not null);
        StarRowCommand = new(value =>
        {
            if (value is not VocabularyItem item || IsBusy) return;
            SelectedItem = item;
            StarCommand.Execute(null);
        }, _ => !IsBusy);
        CategoryScope.Changed += () => SearchCommand.Execute(null);
        PropertyChanged += (_, args) => { if (args.PropertyName == nameof(IsBusy)) { OnPropertyChanged(nameof(CanChangeCategory)); StarRowCommand.NotifyCanExecuteChanged(); } };
        AddCategoryCommand = Command(AddCategoryAsync);
        PauseCommand = Command(TogglePauseAsync, () => SelectedItem is { IsArchived: false });
        ArchiveCommand = Command(ArchiveAsync, () => SelectedItem is { IsArchived: false });
        RestoreCommand = Command(RestoreAsync, () => SelectedItem is { IsArchived: true });
        NewCommand = new(_ => edit(null));
        EditCommand = new(_ => edit(SelectedItem), _ => SelectedItem is not null);
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppDiagnostics.Write($"Library load started: searchLength={SearchText.Length}; filter={SelectedFilter}; selectedCategories={CategoryScope.SelectedNames.Length}");
        var index = Math.Max(0, Items.IndexOf(SelectedItem!));
        var selectedId = SelectedItem?.Id;
        var categories = await store.GetCategoriesAsync(cancellationToken);
        AppDiagnostics.Write($"Library categories loaded: count={categories.Count}");
        Categories.Clear();
        Categories.Add("全部分類");
        foreach (var category in categories) Categories.Add(category);
        CategoryScope.SetAvailable(categories);
        var selected = CategoryScope.SelectedNames;
        var vocabulary = await store.GetVocabularyAsync(null,
            null, cancellationToken,
            includeArchived: SelectedFilter == "已封存");
        var eligible = vocabulary.Where(item => SelectedFilter switch
        {
            "學習中" => !item.IsArchived && !item.IsPaused && item.Enrollment != Enrollment.Skipped,
            "已暫停" => !item.IsArchived && (item.IsPaused || item.Enrollment == Enrollment.Skipped),
            "已封存" => item.IsArchived,
            _ => true
        }).ToList();
        var text = SearchText.Trim();
        var matchingWords = eligible.Where(item =>
            (selected.Length == 0 || item.Categories.Intersect(selected, StringComparer.OrdinalIgnoreCase).Any()) &&
            item.MatchesSearch(text))
            .Select(x => x.WordId).ToHashSet();
        var filtered = eligible.Where(x => matchingWords.Contains(x.WordId)).ToList();
        filtered = SortItems(filtered, SelectedSort).ToList();
        Items.Clear();
        foreach (var item in filtered) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(x => x.Id == selectedId) ?? (Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)]);
        OnPropertyChanged(nameof(CountText));
        AppDiagnostics.Write($"Library loaded: queried={vocabulary.Count}; displayed={Items.Count}");
    }

    public static IEnumerable<VocabularyItem> SortItems(IEnumerable<VocabularyItem> items, string sort) => sort switch
    {
        "建立時間：最早優先" => items.OrderBy(x => x.CreatedAt).ThenBy(x => x.CreationOrder),
        "單字：A–Z" => items.OrderBy(x => x.Headword, StringComparer.OrdinalIgnoreCase),
        "單字：Z–A" => items.OrderByDescending(x => x.Headword, StringComparer.OrdinalIgnoreCase),
        "熟練程度：低到高" => items.OrderBy(x => x.Stability).ThenBy(x => x.Headword, StringComparer.OrdinalIgnoreCase),
        "熟練程度：高到低" => items.OrderByDescending(x => x.Stability).ThenBy(x => x.Headword, StringComparer.OrdinalIgnoreCase),
        "星號優先" => items.OrderByDescending(x => x.IsStarred).ThenBy(x => x.Headword, StringComparer.OrdinalIgnoreCase),
        _ => items.Reverse().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.CreationOrder)
    };

    private async Task AddCategoryAsync(CancellationToken token)
    {
        var name = NewCategory.Trim();
        if (name.Length is < 1 or > 40) throw new InvalidOperationException("分類名稱需為 1–40 個字。");
        await store.AddCategoryAsync(name, token);
        NewCategory = "";
        await LoadAsync(token);
        Notice = $"已建立分類「{name}」。編輯詞義時可加入這個分類。";
    }

    private async Task TogglePauseAsync(CancellationToken token)
    {
        if (SelectedItem is not { } item) return;
        var wasPaused = item.IsPaused || item.Enrollment == Enrollment.Skipped;
        if (item.Enrollment == Enrollment.Skipped)
            await store.SaveVocabularyAsync(item with { Enrollment = Enrollment.Selected, IsPaused = false }, token);
        else
            await store.SetPausedAsync(item.Id, !wasPaused, token);
        await LoadAsync(token);
        Notice = wasPaused ? "已恢復，原有進度保留。" : "已暫停，原有進度保留。";
    }

    private async Task ArchiveAsync(CancellationToken token)
    {
        if (SelectedItem is not { } item) return;
        await store.ArchiveAsync(item.Id, token);
        await LoadAsync(token);
        Notice = "已封存詞義並保留學習歷史，可從「已封存」篩選查看與還原。";
    }

    private async Task RestoreAsync(CancellationToken token)
    {
        if (SelectedItem is not { IsArchived: true } item) return;
        await store.SaveVocabularyAsync(item with { IsArchived = false }, token);
        await LoadAsync(token);
        Notice = "已還原詞義，原有學習進度與暫停狀態保留。";
    }
}
