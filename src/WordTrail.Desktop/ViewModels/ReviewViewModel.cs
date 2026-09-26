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
    private Guid? lastOperationId;
    private int completed;
    private string emptyText = "正在準備學習卡…";

    public ReviewItem? Current { get => current; private set { SetProperty(ref current, value); OnPropertyChanged(nameof(HasCard)); OnPropertyChanged(nameof(OriginText)); NotifyCommands(); } }
    public bool HasCard => Current is not null;
    public bool IsAnswerVisible { get => isAnswerVisible; private set { SetProperty(ref isAnswerVisible, value); NotifyCommands(); } }
    public string ProgressText => $"這次已複習 {completed} 個詞義";
    public string EmptyText { get => emptyText; private set => SetProperty(ref emptyText, value); }
    public string SpeechStatus => speech.Status;
    public string OriginText => Current is null ? "" : $"內容來源：{Current.Word.Origin.Note}";
    public AsyncCommand AgainCommand { get; }
    public AsyncCommand HardCommand { get; }
    public AsyncCommand GoodCommand { get; }
    public AsyncCommand EasyCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand UndoCommand { get; }
    public RelayCommand FlipCommand { get; }
    public RelayCommand SpeakCommand { get; }
    public RelayCommand StopSpeechCommand { get; }
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
        RefreshCommand = Command(LoadAsync, () => !isSubmitting);
        UndoCommand = Command(UndoAsync, () => lastOperationId.HasValue && !isSubmitting);
        FlipCommand = new(_ => IsAnswerVisible = true, _ => Current is not null && !IsAnswerVisible && !isSubmitting);
        SpeakCommand = new(_ => Speak(), _ => Current is not null);
        StopSpeechCommand = new(_ => speech.Stop());
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

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        speech.Stop();
        var next = await store.GetNextReviewAsync(DateTimeOffset.UtcNow, settings.DailyNewLimit, cancellationToken);
        Current = next;
        IsAnswerVisible = false;
        operationId = Guid.NewGuid();
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
        isSubmitting = true;
        NotifyCommands();
        try
        {
            // 此 operationId 保留到成功保存，失敗重試不製造重複評分。
            var result = await store.SubmitReviewAsync(new(card.Word.Id, rating, DateTimeOffset.UtcNow,
                operationId, card.ScheduleVersion), token);
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
        if (Current is null) return;
        try { speech.Speak(Current.Word.Headword); }
        catch (Exception exception) { Error = exception.Message; }
    }

    private void NotifyCommands()
    {
        foreach (var command in RatingCommands) command?.NotifyCanExecuteChanged();
        UndoCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        FlipCommand?.NotifyCanExecuteChanged();
        SpeakCommand?.NotifyCanExecuteChanged();
        DictionaryCommand?.NotifyCanExecuteChanged();
    }
}
