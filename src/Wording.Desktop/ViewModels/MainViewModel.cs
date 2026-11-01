using System.Windows;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IStudyStore store;
    private readonly IContentGenerator generator;
    private PageViewModel currentPage;
    private string currentSection = "今日學習";
    private bool isNavigating;
    public DashboardViewModel Dashboard { get; }
    public LibraryViewModel Library { get; }
    public ReviewViewModel Review { get; }
    public PracticeViewModel? Practice { get; }
    public TranslationViewModel? Translation { get; }
    public SettingsViewModel Settings { get; }
    public UpdateViewModel Updates { get; }
    public string AppVersion => typeof(MainViewModel).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "";
    public PageViewModel CurrentPage { get => currentPage; private set => SetProperty(ref currentPage, value); }
    public string CurrentSection
    {
        get => currentSection;
        private set
        {
            SetProperty(ref currentSection, value);
            OnPropertyChanged(nameof(IsTodaySelected));
            OnPropertyChanged(nameof(IsLibrarySelected));
            OnPropertyChanged(nameof(IsReviewSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsPracticeSelected));
            OnPropertyChanged(nameof(IsTranslationSelected));
            OnPropertyChanged(nameof(ShowUpdateBanner));
        }
    }
    public bool IsTodaySelected => CurrentSection == "今日學習";
    public bool IsLibrarySelected => CurrentSection is "單字庫" or "新增詞義" or "編輯詞義";
    public bool IsReviewSelected => CurrentSection == "複習";
    public bool IsSettingsSelected => CurrentSection == "設定與備份";
    public bool IsPracticeSelected => CurrentSection == "情境練習";
    public bool IsTranslationSelected => CurrentSection == "中翻英練習";
    public bool ShowUpdateBanner => Updates.ShowBanner && !IsSettingsSelected;
    public RelayCommand NavigateCommand { get; }

    public MainViewModel(IStudyStore store, IBackupService backup, IContentGenerator generator,
        IPronunciationService speech, AppSettings settings, AiSettings aiSettings, string dataDirectory,
        IApplicationUpdateClient? updateClient = null, IUpdateInstaller? updateInstaller = null)
    {
        this.store = store;
        this.generator = generator;
        Dashboard = new(store, () => Navigate("review"), () => Navigate("library"));
        Library = new(store, OpenEditor);
        Review = new(store, speech, settings);
        if (store is IPracticeStore practiceStore && generator is IPracticeGenerator practiceGenerator && speech is IPracticeSpeech practiceSpeech)
            Practice = new(store, practiceStore, practiceGenerator, practiceSpeech, settings, () => settings.Save(dataDirectory), OpenEditor);
        if (store is IPracticeStore translationStore && generator is ITranslationGenerator translationGenerator)
            Translation = new(translationStore, translationGenerator);
        Settings = new(backup, generator, speech, settings, aiSettings, dataDirectory, () => { Review.EndSession(); Practice?.Reset(); Translation?.Reset(); }, store);
        currentPage = Dashboard;
        var version = typeof(MainViewModel).Assembly.GetName().Version!;
        Updates = new(updateClient ?? new GitHubUpdateClient(), updateInstaller ?? new UpdateInstaller(), backup, settings,
            new(version.Major, version.Minor, version.Build), dataDirectory, () => settings.Save(dataDirectory),
            RestartBlockReason,
            () => Application.Current.Shutdown());
        Settings.Updates = Updates;
        Updates.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateViewModel.ShowBanner)) OnPropertyChanged(nameof(ShowUpdateBanner));
        };
        NavigateCommand = new(parameter => Navigate(parameter as string ?? "today"));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await Dashboard.LoadAsync();
            AppDiagnostics.Write($"Dashboard loaded: total={Dashboard.Summary.TotalCount}");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write($"Dashboard error: {exception}");
            Dashboard.Error = exception.Message;
        }
    }

    private async void Navigate(string section)
    {
        if (isNavigating || !Updates.CanUseApplication) return;
        if (CurrentPage.IsBusy) { CurrentPage.Notice = "正在處理，完成或取消後即可切換畫面。"; return; }
        if (!ConfirmLeaveEditor()) return;
        isNavigating = true;
        try
        {
            if (CurrentPage == Review) await Review.EndSessionAsync();
            if (CurrentPage == Practice) Practice?.StopSpeech();
            var page = section switch
            {
                "library" => (PageViewModel)Library,
                "review" => Review,
                "settings" => Settings,
                "practice" when Practice is not null => Practice,
                "translation" when Translation is not null => Translation,
                _ => Dashboard
            };
            CurrentSection = section switch
            {
                "library" => "單字庫", "review" => "複習", "practice" when Practice is not null => "情境練習",
                "translation" when Translation is not null => "中翻英練習", "settings" => "設定與備份", _ => "今日學習"
            };
            if (page == Review) Review.BeginSession();
            CurrentPage = page;
            AppDiagnostics.Write($"Navigation loading: {section}");
            await page.LoadAsync();
            AppDiagnostics.Write($"Navigation loaded: {section}");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write($"Navigation error: {section}; {exception}");
            CurrentPage.Error = exception.Message;
        }
        finally { isNavigating = false; }
    }

    private async void OpenEditor(VocabularyItem? item)
    {
        if (CurrentPage.IsBusy) return;
        if (CurrentPage == Practice) Practice?.StopSpeech();
        var editor = new EditorViewModel(store, generator, item, _ => { }, () => Navigate("library"),
            () => Application.Current.Dispatcher.BeginInvoke(() => OpenEditor(null)));
        CurrentSection = item is null ? "新增詞義" : "編輯詞義";
        CurrentPage = editor;
        try { await editor.LoadAsync(); }
        catch (Exception exception) { editor.Error = exception.Message; }
    }

    public bool ConfirmLeaveEditor() => CurrentPage is not EditorViewModel { IsDirty: true }
        || Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.LeaveEditor);

    private string? RestartBlockReason()
    {
        if (CurrentPage.IsBusy) return "請等目前操作完成後再安裝更新。";
        if (CurrentPage is EditorViewModel { IsDirty: true }) return "請先保存正在編輯的詞義，再安裝更新。";
        if (Practice?.IsUnsubmitted == true && !Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.UpdatePractice))
            return "已保留目前練習，可完成作答後再更新。";
        if (Translation?.IsUnsubmitted == true && !Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.UpdateTranslation))
            return "已保留中翻英練習，可完成作答後再更新。";
        return null;
    }
}
