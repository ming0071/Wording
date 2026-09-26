using WordTrail.Core;
using WordTrail.Infrastructure;

namespace WordTrail.Desktop.ViewModels;

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

    public ReviewItem? Current
    {
        get => current;
        private set
        {
            SetProperty(ref current, value);
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(OriginText));
            OnPropertyChanged(nameof(MeaningOriginText));
            OnPropertyChanged(nameof(CollocationsOriginText));
            OnPropertyChanged(nameof(Examples));
            NotifyCommands();
        }
    }
    public bool HasCard => Current is not null;
    public bool IsAnswerVisible { get => isAnswerVisible; private set { SetProperty(ref isAnswerVisible, value); NotifyCommands(); } }
    public string ProgressText => $"這次已複習 {completed} 個詞義";
    public string EmptyText { get => emptyText; private set => SetProperty(ref emptyText, value); }
    public string SpeechStatus => speech.Status;
    public string OriginText => Current is null ? "" : $"詞義初始來源：{Current.Word.Origin.Note}";
    public string MeaningOriginText => Current is null ? "" : $"解釋来源：{(Current.Word.MeaningOrigin ?? Current.Word.Origin).Note}";
    public string CollocationsOriginText => Current is null ? "" : $"搭配來源：{(Current.Word.CollocationsOrigin ?? Current.Word.Origin).Note}";
    public IReadOnlyList<ReviewExample> Examples => Current?.Word.Examples.Select(example =>
        new ReviewExample(example.English, example.Chinese, $"例句來源：{(example.Origin ?? Current.Word.Origin).Note}")).ToArray() ?? [];
    public AsyncCommand AgainCommand { get; }
    public AsyncCommand HardCommand { get; }
    public AsyncCommand GoodCommand { get; }
    public AsyncCommand EasyCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand UndoCommand { get; }
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
        AgainCommand = Command(token => RateAsync(ReviewRating.Again, token), CanRate);
        HardCommand = Command(token => RateAsync(ReviewRating.Hard, token), CanRate);
        GoodCommand = Command(token => RateAsync(ReviewRating.Good, token), CanRate);
        EasyCommand = Command(token => RateAsync(ReviewRating.Easy, token), CanRate);
        RefreshCommand = Command(async token =>
        {
            if (pendingSubmission is not null) lastOperationId = null;
            await LoadAsync(token);
        }, () => !isSubmitting);
        UndoCommand = Command(UndoAsync, () => lastOperationId.HasValue && !isSubmitting && pendingSubmission is null);
        FlipCommand = new(_ => IsAnswerVisible = true, _ => Current is not null && !IsAnswerVisible && !isSubmitting);
        SpeakCommand = new(_ => Speak(), _ => Current is not null);
        StopSpeechCommand = new(_ => speech.Stop());
        SpeakExampleCommand = new(_ => SpeakText(Current?.Word.Examples.FirstOrDefault()?.English),
            _ => Current?.Word.Examples.Length > 0 && IsAnswerVisible);
        DictionaryCommand = new(_ => UiActions.OpenDictionary(Current?.Word.Headword, message => Error = message), _ => Current is not null);
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
        speech.Stop();
        var next = await store.GetNextReviewAsync(DateTimeOffset.UtcNow, settings.DailyNewLimit, cancellationToken);
        Current = next;
        IsAnswerVisible = false;
        operationId = Guid.NewGuid();
        pendingSubmission = null;
        NotifyCommands();
        if (next is null)
        {
            var summary = await store.GetDashboardAsync(DateTimeOffset.UtcNow, cancellationToken);
            EmptyText = summary.NextDue is { } due && due > DateTimeOffset.UtcNow
                ? $"目前沒有可複習的卡片。下一張在 {due.ToLocalTime():HH:mm} 到期，再按「重新檢查」即可。"
                : "今天可以先休息了。若想加入新詞，可到單字庫選入；每日新詞配額用完後，明天再開始。";
        }
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

    private void SpeakText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { speech.Speak(text); }
        catch (Exception exception) { Error = exception.Message; }
    }

    private void NotifyCommands()
    {
        foreach (var command in RatingCommands) command?.NotifyCanExecuteChanged();
        UndoCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        FlipCommand?.NotifyCanExecuteChanged();
        SpeakCommand?.NotifyCanExecuteChanged();
        SpeakExampleCommand?.NotifyCanExecuteChanged();
        DictionaryCommand?.NotifyCanExecuteChanged();
    }
}

public sealed record ReviewExample(string English, string Chinese, string Source);
