using System.Collections.ObjectModel;
using System.Windows.Input;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed class ReviewViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private readonly IPronunciationService speech;
    private readonly AppSettings settings;
    private ReviewItem? current;
    private bool isAnswerVisible;
    private bool isSubmitting;
    private Guid operationId = Guid.NewGuid();
    private ReviewSubmission? pendingSubmission;
    private Guid? lastOperationId;
    private int completed;
    private string emptyText = "正在準備學習卡…";
    private string[] reviewCategories = [];
    private string scopeText = "";
    private IReadOnlyList<ReviewDefinitionViewModel> definitions = [];
    private double definitionWidth = double.PositiveInfinity;

    public ObservableCollection<string> Categories { get; } = [];
    public CategorySelection CategoryScope { get; } = new("全部單字庫");
    public string SelectedCategory { get => CategoryScope.SelectedNames.FirstOrDefault() ?? "全部單字庫";
        set => CategoryScope.SetSelected(value == "全部單字庫" ? [] : [value]); }
    public string ScopeText { get => scopeText; private set => SetProperty(ref scopeText, value); }
    public bool CanChangeCategory => !IsBusy && !isSubmitting && pendingSubmission is null;

    public ReviewItem? Current
    {
        get => current;
        private set
        {
            SetProperty(ref current, value);
            definitions = value?.Word.Definitions.Select(x => new ReviewDefinitionViewModel(x)).ToArray() ?? [];
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(Definitions));
            OnPropertyChanged(nameof(PartOfSpeechText));
            OnPropertyChanged(nameof(DefinitionColumns));
            OnPropertyChanged(nameof(HasParallelDefinitions));
            OnPropertyChanged(nameof(HasOtherSenses));
            OnPropertyChanged(nameof(SenseContext));
            OnPropertyChanged(nameof(Examples));
            OnPropertyChanged(nameof(SynonymsText));
            OnPropertyChanged(nameof(CollocationsText));
            OnPropertyChanged(nameof(HasSynonyms));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(HasEnglishDefinition));
            OnPropertyChanged(nameof(HasCollocations));
            NotifyCommands();
        }
    }
    public bool HasCard => Current is not null;
    public bool IsAnswerVisible { get => isAnswerVisible; private set { SetProperty(ref isAnswerVisible, value); OnPropertyChanged(nameof(IsQuestionVisible)); NotifyCommands(); } }
    public bool IsQuestionVisible => !IsAnswerVisible;
    private static string KeyLabel(string key) => key == "Space" ? "空白鍵" : key;
    public string FlipButtonText => $"顯示答案 · {KeyLabel(settings.FlipKey)}";
    public string AgainButtonText => $"重來 · {KeyLabel(settings.AgainKey)}";
    public string HardButtonText => $"困難 · {KeyLabel(settings.HardKey)}";
    public string GoodButtonText => $"良好 · {KeyLabel(settings.GoodKey)}";
    public string EasyButtonText => $"簡單 · {KeyLabel(settings.EasyKey)}";
    public string SpeakButtonText => $"朗讀單字 · {KeyLabel(settings.SpeakKey)}";
    public string SpeakExampleButtonText => $"朗讀例句 · {KeyLabel(settings.SpeakExampleKey)}";
    public string SynonymsText => string.Join("、", Current?.Word.Synonyms ?? []);
    public string CollocationsText => string.Join(" · ", Current?.Word.Collocations ?? []);
    public bool HasSynonyms => Current?.Word.Synonyms.Length > 0;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Current?.Word.Notes);
    public bool HasEnglishDefinition => !string.IsNullOrWhiteSpace(Current?.Word.EnglishDefinition);
    public bool HasCollocations => Current?.Word.Collocations.Length > 0;
    public IReadOnlyList<VocabularyDefinition> OtherSenses => Current?.Word.AdditionalSenses ?? [];
    public bool HasOtherSenses => OtherSenses.Count > 0;
    public IReadOnlyList<ReviewDefinitionViewModel> Definitions => definitions;
    public string PartOfSpeechText => Definitions.FirstOrDefault()?.PartOfSpeech ?? "";
    public int DefinitionColumns => Math.Min(Math.Clamp(Definitions.Count, 1, 3),
        double.IsPositiveInfinity(definitionWidth) ? 3 : Math.Clamp((int)(definitionWidth / 360), 1, 3));
    public bool HasParallelDefinitions => DefinitionColumns > 1;
    public void UpdateDefinitionWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var previous = DefinitionColumns;
        definitionWidth = width;
        if (previous == DefinitionColumns) return;
        OnPropertyChanged(nameof(DefinitionColumns));
        OnPropertyChanged(nameof(HasParallelDefinitions));
    }
    public string SenseContext => Current is { } card && card.Word.Definitions.Count > 1
        ? $"這個詞有 {card.Word.Definitions.Count} 組解釋 · 一起複習並評分"
        : "";
    public string KeyboardHint => $"{KeyLabel(settings.FlipKey)} 翻卡 · {settings.AgainKey}／{settings.HardKey}／{settings.GoodKey}／{settings.EasyKey} 評分 · {settings.SpeakKey} 讀單字 · {settings.SpeakExampleKey} 讀例句";
    public ICommand? CommandForShortcut(string key) => key == settings.FlipKey ? FlipCommand :
        key == settings.AgainKey ? AgainCommand : key == settings.HardKey ? HardCommand :
        key == settings.GoodKey ? GoodCommand : key == settings.EasyKey ? EasyCommand :
        key == settings.SpeakKey ? SpeakCommand : key == settings.SpeakExampleKey ? SpeakExampleCommand : null;
    public string ProgressText => $"這次已複習 {completed} 個詞義";
    public string EmptyText { get => emptyText; private set => SetProperty(ref emptyText, value); }
    public string SpeechStatus => speech.Status;
    public IReadOnlyList<ExampleSentence> Examples => Current?.Word.Examples ?? [];
    public AsyncCommand AgainCommand { get; }
    public AsyncCommand HardCommand { get; }
    public AsyncCommand GoodCommand { get; }
    public AsyncCommand EasyCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCategoryCommand { get; }
    public AsyncCommand UndoCommand { get; }
    public AsyncCommand StarCommand { get; }
    public RelayCommand FlipCommand { get; }
    public RelayCommand SpeakCommand { get; }
    public RelayCommand StopSpeechCommand { get; }
    public RelayCommand SpeakExampleCommand { get; }
    public RelayCommand DictionaryCommand { get; }
    private AsyncCommand[] RatingCommands => [AgainCommand, HardCommand, GoodCommand, EasyCommand];

    public ReviewViewModel(IStudyStore store, IPronunciationService speech, AppSettings settings)
    {
        this.store = store;
        this.speech = speech;
        this.settings = settings;
        StarCommand = Command(async token =>
        {
            if (Current is not { } card) return;
            await store.SetStarredAsync(card.Word.Id, !card.Word.IsStarred, token);
            Current = card with { Word = card.Word with { IsStarred = !card.Word.IsStarred } };
        }, () => Current is not null && !isSubmitting && pendingSubmission is null);
        AgainCommand = Command(token => RateAsync(ReviewRating.Again, token), CanRate);
        HardCommand = Command(token => RateAsync(ReviewRating.Hard, token), CanRate);
        GoodCommand = Command(token => RateAsync(ReviewRating.Good, token), CanRate);
        EasyCommand = Command(token => RateAsync(ReviewRating.Easy, token), CanRate);
        RefreshCommand = Command(async token =>
        {
            if (pendingSubmission is not null) lastOperationId = null;
            await LoadAsync(token);
        }, () => !isSubmitting);
        ApplyCategoryCommand = Command(async token =>
        {
            try { await LoadCardAsync(CategoryScope.SelectedNames, token); }
            catch { CategoryScope.SetSelected(reviewCategories); throw; }
            Notice = "已切換複習範圍；到期卡優先，新詞依每日上限提供。";
        }, () => !isSubmitting && pendingSubmission is null);
        CategoryScope.Changed += () => ApplyCategoryCommand.Execute(null);
        UndoCommand = Command(UndoAsync, () => lastOperationId.HasValue && !isSubmitting && pendingSubmission is null);
        FlipCommand = new(_ =>
        {
            IsAnswerVisible = true;
            if (settings.AutoSpeakExamples) SpeakExamples();
        }, _ => Current is not null && !IsAnswerVisible && !isSubmitting);
        SpeakCommand = new(_ => Speak(), _ => Current is not null);
        StopSpeechCommand = new(_ => speech.Stop());
        SpeakExampleCommand = new(_ => SpeakExamples(),
            _ => Definitions.Any(x => x.Examples.Count > 0) && IsAnswerVisible);
        DictionaryCommand = new(_ => UiActions.OpenDictionary(Current?.Word.Headword, message => Error = message), _ => Current is not null);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IsBusy)) OnPropertyChanged(nameof(CanChangeCategory));
        };
    }

    public void BeginSession()
    {
        lastOperationId = null;
        completed = 0;
        Notice = "";
        Error = "";
        OnPropertyChanged(nameof(ProgressText));
        NotifyCommands();
    }

    public void EndSession()
    {
        speech.Stop();
        lastOperationId = null;
        NotifyCommands();
    }

    public async Task EndSessionAsync()
    {
        await store.EndReviewSessionAsync();
        EndSession();
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var name in new[] { nameof(FlipButtonText), nameof(AgainButtonText), nameof(HardButtonText),
                     nameof(GoodButtonText), nameof(EasyButtonText), nameof(KeyboardHint), nameof(SpeakButtonText), nameof(SpeakExampleButtonText) }) OnPropertyChanged(name);
        var categories = await store.GetCategoriesAsync(cancellationToken);
        Categories.Clear();
        Categories.Add("全部單字庫");
        foreach (var category in categories) Categories.Add(category);
        CategoryScope.SetAvailable(categories);
        CategoryScope.SetSelected(reviewCategories);
        await LoadCardAsync(CategoryScope.SelectedNames, cancellationToken);
    }

    private async Task LoadCardAsync(string[] categories, CancellationToken cancellationToken)
    {
        speech.Stop();
        var now = DateTimeOffset.UtcNow;
        var category = categories.Length == 1 ? categories[0] : null;
        var next = await store.GetNextReviewAsync(now, settings.DailyNewLimit, cancellationToken, category, categories);
        var summary = await store.GetDashboardAsync(now, cancellationToken, category, categories);
        reviewCategories = categories;
        ScopeText = $"{(categories.Length == 0 ? "全部單字庫" : string.Join("、", categories))} · {summary.DueCount} 個到期 · {summary.NewCount} 個未學新詞";
        Current = next;
        OnPropertyChanged(nameof(OtherSenses));
        OnPropertyChanged(nameof(HasOtherSenses));
        OnPropertyChanged(nameof(SenseContext));
        IsAnswerVisible = false;
        operationId = Guid.NewGuid();
        pendingSubmission = null;
        NotifyCommands();
        if (next is null)
        {
            var nextDueText = summary.NextDue is { } due && due > now
                ? $"下一張在 {due.ToLocalTime():MM/dd HH:mm} 到期，再按「重新檢查」即可。" : "";
            EmptyText = summary.NewCount > 0
                ? "這個範圍今天的新詞名額已用完、新詞上限設為 0，或因到期複習量而減量。可在設定查看每日上限，或明天繼續。" + nextDueText
                : nextDueText.Length > 0 ? "這個範圍目前沒有可複習的卡片。" + nextDueText
                    : "這個範圍目前沒有到期卡或未學新詞。可以切換主題，或到單字庫新增詞義。";
        }
        else if (settings.AutoSpeakWord) Speak();
    }

    private bool CanRate() => Current is not null && IsAnswerVisible && !isSubmitting;

    private async Task RateAsync(ReviewRating rating, CancellationToken token)
    {
        if (!CanRate() || Current is not { } card) return;
        if (pendingSubmission is { } pending && pending.Rating != rating)
            throw new InvalidOperationException("上次評分尚未確認保存。請重按同一個評分以重試，或按「重新取卡」確認目前資料；不要改用另一個評分重送。");
        isSubmitting = true;
        NotifyCommands();
        try
        {
            // 結果不確定時重送同一份 payload，包含評分、時間與版本，不只保留 ID。
            pendingSubmission ??= new(card.Word.Id, rating, DateTimeOffset.UtcNow, operationId, card.ScheduleVersion);
            var result = await store.SubmitReviewAsync(pendingSubmission, token);
            pendingSubmission = null;
            lastOperationId = result.OperationId;
            completed++;
            OnPropertyChanged(nameof(ProgressText));
            Current = null;
            IsAnswerVisible = false;
            await LoadAsync(token);
            Notice = $"已保存。下一次複習：{result.DueAt.ToLocalTime():MM/dd HH:mm}";
        }
        finally { isSubmitting = false; NotifyCommands(); }
    }

    private async Task UndoAsync(CancellationToken token)
    {
        if (lastOperationId is not { } last || isSubmitting) return;
        isSubmitting = true;
        NotifyCommands();
        try
        {
            await store.UndoReviewAsync(last, token);
            lastOperationId = null;
            completed = Math.Max(0, completed - 1);
            OnPropertyChanged(nameof(ProgressText));
            await LoadAsync(token);
            Notice = "已撤銷最後一次評分，今天已開始的新詞名額仍保留。";
        }
        finally { isSubmitting = false; NotifyCommands(); }
    }

    private void Speak()
    {
        SpeakText(Current?.Word.Headword);
    }
    private void SpeakExamples() => SpeakText(string.Join(" ", Definitions.SelectMany(x => x.Examples).Select(x => x.English)));

    private void SpeakText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { speech.Speak(text); }
        catch (Exception exception) { Error = exception.Message; }
    }

    private void NotifyCommands()
    {
        StarCommand?.NotifyCanExecuteChanged();
        foreach (var command in RatingCommands) command?.NotifyCanExecuteChanged();
        UndoCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        ApplyCategoryCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeCategory));
        FlipCommand?.NotifyCanExecuteChanged();
        SpeakCommand?.NotifyCanExecuteChanged();
        SpeakExampleCommand?.NotifyCanExecuteChanged();
        DictionaryCommand?.NotifyCanExecuteChanged();
    }
}
