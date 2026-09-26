using WordTrail.Core;

namespace WordTrail.Desktop.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly IStudyStore store;
    private DashboardSummary summary = new(0, 0, 0, 0, 0, null);
    public DashboardSummary Summary { get => summary; private set { SetProperty(ref summary, value); OnPropertyChanged(nameof(NextDueText)); } }
    public string NextDueText => Summary.NextDue is { } next
        ? $"下一張學習卡：{next.ToLocalTime():MM/dd HH:mm}" : "現在沒有稍後到期的學習卡。";
    public string Greeting => $"{DateTime.Now:MM 月 dd 日}，留一點時間給英文。";
    public RelayCommand StartReviewCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public DashboardViewModel(IStudyStore store, Action startReview, Action browse)
    {
        this.store = store;
        StartReviewCommand = new(_ => startReview());
        BrowseCommand = new(_ => browse());
        RefreshCommand = Command(LoadAsync);
    }

    public override async Task LoadAsync(CancellationToken cancellationToken = default) =>
        Summary = await store.GetDashboardAsync(DateTimeOffset.UtcNow, cancellationToken);
}
