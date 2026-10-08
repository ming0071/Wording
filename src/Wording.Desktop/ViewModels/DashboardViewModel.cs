using Wording.Core;

namespace Wording.Desktop.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private DashboardSummary summary = new(0, 0, 0, 0, 0, null);
    public DashboardSummary Summary { get => summary; private set { SetProperty(ref summary, value); OnPropertyChanged(nameof(NextDueText)); } }
    public string NextDueText => Summary.NextDue is { } next
        ? $"下一張學習卡：{next.ToLocalTime():MM/dd HH:mm}" : "現在沒有稍後到期的學習卡。";
    public string NewWordHelp => $"單字庫中的新詞會自動加入學習。先複習到期卡，再依每日上限認識新詞；可在複習頁選擇主題或全部單字庫。複習頁預設每天最多 {ApplicationConfiguration.Current.Review.DailyNewLimit} 個新詞，可在設定調整；到期卡數不會降低新詞上限，可隨時停止學習。情境練習的新詞評分計入今日數量，但可超過上限。";
    public string Greeting => $"{DateTime.Now:MM 月 dd 日}，留一點時間給英文。";
    public RelayCommand StartReviewCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    private LearningActivity activity = LearningActivity.Build([], DateOnly.FromDateTime(DateTime.Now));
    public LearningActivity Activity { get => activity; private set { SetProperty(ref activity, value); OnPropertyChanged(nameof(ActivitySummary)); } }
    public string ActivitySummary => $"近一年累積複習 {Activity.Total} 詞次 · 學習 {Activity.ActiveDays} 天 · 連續 {Activity.Streak} 天";
    private string selectedDayText = "點選格子查看當天成果。";
    public string SelectedDayText { get => selectedDayText; private set => SetProperty(ref selectedDayText, value); }
    public RelayCommand SelectActivityDayCommand { get; }

    public DashboardViewModel(IStudyStore store, Action startReview, Action browse)
    {
        this.store = store;
        StartReviewCommand = new(_ => startReview());
        BrowseCommand = new(_ => browse());
        RefreshCommand = Command(LoadAsync);
        SelectActivityDayCommand = new(value => { if (value is ActivityCell cell) SelectedDayText = cell.Description; });
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Summary = await store.GetDashboardAsync(DateTimeOffset.UtcNow, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Now);
        Activity = LearningActivity.Build(await store.GetStudyActivityAsync(today.AddDays(-364), today, cancellationToken), today);
        OnPropertyChanged(nameof(Greeting));
    }
}
