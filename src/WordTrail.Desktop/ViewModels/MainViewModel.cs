using WordTrail.Core;
using WordTrail.Infrastructure;

namespace WordTrail.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IStudyStore store;
    private readonly IContentGenerator generator;
    private PageViewModel currentPage;
    private string currentSection = "今日學習";
    public DashboardViewModel Dashboard { get; }
    public LibraryViewModel Library { get; }
    public ReviewViewModel Review { get; }
    public SettingsViewModel Settings { get; }
    public PageViewModel CurrentPage { get => currentPage; private set => SetProperty(ref currentPage, value); }
    public string CurrentSection { get => currentSection; private set => SetProperty(ref currentSection, value); }
    public RelayCommand NavigateCommand { get; }

    public MainViewModel(IStudyStore store, IBackupService backup, IContentGenerator generator,
        IPronunciationService speech, AppSettings settings, AiSettings aiSettings, string dataDirectory)
    {
        this.store = store;
        this.generator = generator;
        Dashboard = new(store, () => Navigate("review"), () => Navigate("library"));
        Library = new(store, OpenEditor);
        Review = new(store, speech, settings);
        Settings = new(backup, generator, speech, settings, aiSettings, dataDirectory, () => Review.EndSession());
        currentPage = Dashboard;
        NavigateCommand = new(parameter => Navigate(parameter as string ?? "today"));
    }

    public async Task InitializeAsync()
    {
        try { await Dashboard.LoadAsync(); }
        catch (Exception exception) { Dashboard.Error = exception.Message; }
    }

    private async void Navigate(string section)
    {
        if (CurrentPage.IsBusy) { CurrentPage.Notice = "正在處理，完成或取消後即可切換畫面。"; return; }
        if (CurrentPage == Review) Review.EndSession();
        var page = section switch
        {
            "library" => (PageViewModel)Library,
            "review" => Review,
            "settings" => Settings,
            _ => Dashboard
        };
        CurrentSection = section switch { "library" => "單字庫", "review" => "複習", "settings" => "設定與備份", _ => "今日學習" };
        if (page == Review) Review.BeginSession();
        CurrentPage = page;
        try { await page.LoadAsync(); }
        catch (Exception exception) { page.Error = exception.Message; }
    }

    private async void OpenEditor(VocabularyItem? item)
    {
        if (CurrentPage.IsBusy) return;
        var editor = new EditorViewModel(store, generator, item, _ => { });
        CurrentSection = item is null ? "新增詞義" : "編輯詞義";
        CurrentPage = editor;
        try { await editor.LoadAsync(); }
        catch (Exception exception) { editor.Error = exception.Message; }
    }
}
