using System.Collections.ObjectModel;
using WordTrail.Core;

namespace WordTrail.Desktop.ViewModels;

public sealed class LibraryViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private readonly Action<VocabularyItem?> edit;
    private string searchText = "";
    private string selectedCategory = "全部分類";
    private string selectedFilter = "待篩選";
    private string newCategory = "";
    private VocabularyItem? selectedItem;

    public ObservableCollection<VocabularyItem> Items { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public string[] Filters { get; } = ["待篩選", "學習中", "已略過", "已暫停", "已封存", "全部"];
    public string SearchText { get => searchText; set => SetProperty(ref searchText, value); }
    public string SelectedCategory { get => selectedCategory; set => SetProperty(ref selectedCategory, value); }
    public string SelectedFilter { get => selectedFilter; set => SetProperty(ref selectedFilter, value); }
    public string NewCategory { get => newCategory; set => SetProperty(ref newCategory, value); }
    public string CountText => $"{Items.Count} 個詞義 · 選入後才會加入複習";
    public string PauseLabel => SelectedItem?.IsPaused == true ? "恢復複習" : "暫停複習";
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
    public AsyncCommand SelectCommand { get; }
    public AsyncCommand SkipCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ArchiveCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    private AsyncCommand[] SelectionCommands => [SelectCommand, SkipCommand, PauseCommand, ArchiveCommand];

    public LibraryViewModel(IStudyStore store, Action<VocabularyItem?> edit)
    {
        this.store = store;
        this.edit = edit;
        SearchCommand = Command(LoadAsync);
        AddCategoryCommand = Command(AddCategoryAsync);
        SelectCommand = Command(token => ChangeEnrollmentAsync(Enrollment.Selected, token), () => SelectedItem is { IsArchived: false });
        SkipCommand = Command(token => ChangeEnrollmentAsync(Enrollment.Skipped, token), () => SelectedItem is { IsArchived: false });
        PauseCommand = Command(TogglePauseAsync, () => SelectedItem is { IsArchived: false });
        ArchiveCommand = Command(ArchiveAsync, () => SelectedItem is { IsArchived: false });
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
        var vocabulary = await store.GetVocabularyAsync(SearchText, SelectedCategory == "全部分類" ? null : SelectedCategory, cancellationToken);
        var filtered = vocabulary.Where(item => SelectedFilter switch
        {
            "待篩選" => !item.IsArchived && item.Enrollment == Enrollment.Candidate,
            "學習中" => !item.IsArchived && !item.IsPaused && item.Enrollment == Enrollment.Selected,
            "已略過" => !item.IsArchived && item.Enrollment == Enrollment.Skipped,
            "已暫停" => !item.IsArchived && item.IsPaused,
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

    private async Task ChangeEnrollmentAsync(Enrollment enrollment, CancellationToken token)
    {
        if (SelectedItem is not { } item) return;
        await store.SetEnrollmentAsync(item.Id, enrollment, token);
        await LoadAsync(token);
        Notice = enrollment == Enrollment.Selected ? $"已將 {item.Headword} 加入學習。" : $"已略過 {item.Headword}，之後仍可選入。";
    }

    private async Task TogglePauseAsync(CancellationToken token)
    {
        if (SelectedItem is not { } item) return;
        await store.SetPausedAsync(item.Id, !item.IsPaused, token);
        await LoadAsync(token);
        Notice = item.IsPaused ? "已恢復，原有進度保留。" : "已暫停，原有進度保留。";
    }

    private async Task ArchiveAsync(CancellationToken token)
    {
        if (SelectedItem is not { } item) return;
        await store.ArchiveAsync(item.Id, token);
        await LoadAsync(token);
        Notice = "已封存詞義並保留學習歷史，可從「已封存」篩選查看。";
    }
}
