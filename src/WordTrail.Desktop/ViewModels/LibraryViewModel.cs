using System.Collections.ObjectModel;
using WordTrail.Core;

namespace WordTrail.Desktop.ViewModels;

public sealed class LibraryViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private readonly Action<VocabularyItem?> edit;
    private string searchText = "";
    private string selectedCategory = "全部分類";
    private string selectedFilter = "全部";
    private string newCategory = "";
    private VocabularyItem? selectedItem;

    public ObservableCollection<VocabularyItem> Items { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public string[] Filters { get; } = ["全部", "學習中", "已暫停", "已封存"];
    public string SearchText { get => searchText; set => SetProperty(ref searchText, value); }
    public string SelectedCategory { get => selectedCategory; set => SetProperty(ref selectedCategory, value); }
    public string SelectedFilter { get => selectedFilter; set => SetProperty(ref selectedFilter, value); }
    public string NewCategory { get => newCategory; set => SetProperty(ref newCategory, value); }
    public string CountText => $"{Items.Count} 個詞義 · 新詞自動加入學習";
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
    private AsyncCommand[] SelectionCommands => [PauseCommand, ArchiveCommand, RestoreCommand];

    public LibraryViewModel(IStudyStore store, Action<VocabularyItem?> edit)
    {
        this.store = store;
        this.edit = edit;
        SearchCommand = Command(LoadAsync);
        AddCategoryCommand = Command(AddCategoryAsync);
        PauseCommand = Command(TogglePauseAsync, () => SelectedItem is { IsArchived: false });
        ArchiveCommand = Command(ArchiveAsync, () => SelectedItem is { IsArchived: false });
        RestoreCommand = Command(RestoreAsync, () => SelectedItem is { IsArchived: true });
        NewCommand = new(_ => edit(null));
        EditCommand = new(_ => edit(SelectedItem), _ => SelectedItem is not null);
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var index = Math.Max(0, Items.IndexOf(SelectedItem!));
        var currentCategory = SelectedCategory;
        var categories = await store.GetCategoriesAsync(cancellationToken);
        Categories.Clear();
        Categories.Add("全部分類");
        foreach (var category in categories) Categories.Add(category);
        SelectedCategory = Categories.Contains(currentCategory) ? currentCategory : "全部分類";
        var vocabulary = await store.GetVocabularyAsync(SearchText,
            SelectedCategory == "全部分類" ? null : SelectedCategory, cancellationToken,
            includeArchived: SelectedFilter == "已封存");
        var filtered = vocabulary.Where(item => SelectedFilter switch
        {
            "學習中" => !item.IsArchived && !item.IsPaused && item.Enrollment != Enrollment.Skipped,
            "已暫停" => !item.IsArchived && (item.IsPaused || item.Enrollment == Enrollment.Skipped),
            "已封存" => item.IsArchived,
            _ => true
        }).ToList();
        Items.Clear();
        foreach (var item in filtered) Items.Add(item);
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        OnPropertyChanged(nameof(CountText));
    }

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
