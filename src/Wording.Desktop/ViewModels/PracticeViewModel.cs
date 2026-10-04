using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed class PracticeChoices<T> where T : struct, Enum
{
    public CategoryChoice[] Choices { get; }
    public T Value { get; private set; }
    public PracticeChoices((T Value, string Label)[] choices, T value, Action? changed = null)
    {
        Value = value;
        Choices = choices.Select(x => new CategoryChoice(x.Label, false, _ =>
        {
            Value = x.Value;
            foreach (var choice in Choices!) choice.IsSelected = choice.Name == x.Label;
            changed?.Invoke();
        }) { IsSelected = x.Value.Equals(value) }).ToArray();
    }
}

public sealed class PracticeQuestionViewModel : ObservableObject
{
    private int selected = -1;
    private bool revealed;
    private bool locked;
    private readonly PracticeQuestion question;
    public string Prompt { get; }
    public string[] Options { get; }
    public int SelectedIndex { get => selected; set { if (!locked && SetProperty(ref selected, value)) Changed?.Invoke(); } }
    public bool CanAnswer => !locked;
    public bool IsRevealed => revealed;
    public bool IsCorrect => selected == question.AnswerIndex;
    public string Result => !revealed ? "" : $"{(IsCorrect ? "答對了" : "再看一次")} · 你的答案：{(char)('A' + selected)} · 正確答案：{(char)('A' + question.AnswerIndex)}";
    public string Explanation => revealed ? question.Explanation : "";
    public string Evidence => revealed ? question.Evidence : "";
    public string OptionExplanations => !revealed ? "" : string.Join("\n", question.OptionExplanations.Select((x, i) => $"{(char)('A' + i)}. {x}"));
    public event Action? Changed;
    public PracticeQuestionViewModel(PracticeQuestion question, int number)
    { this.question = question; Prompt = $"{number}. {question.Prompt}"; Options = question.Options.Select((x, i) => $"{(char)('A' + i)}. {x}").ToArray(); }
    public void Lock() { locked = true; OnPropertyChanged(nameof(CanAnswer)); }
    public void Reveal() { Lock(); revealed = true; OnPropertyChanged(""); }
}

public sealed class PracticeTargetViewModel : ObservableObject
{
    private PracticeEligibility? eligibility;
    public VocabularyItem Word { get; private set; }
    internal void UpdateWord(VocabularyItem word) { Word = word; OnPropertyChanged(nameof(Word)); }
    public PracticeUsage Usage { get; }
    public PracticeEligibility? Eligibility { get => eligibility; set { eligibility = value; OnPropertyChanged(""); } }
    internal ReviewSubmission? Pending { get; set; }
    public string Status => Eligibility?.Message ?? "正在檢查評分資格…";
    public string StarText => Eligibility?.IsStarred == true ? "已標星" : "標為不熟悉";
    public bool CanOfferRating => Pending is not null || Eligibility?.CanRate == true || Eligibility?.IsNewLimitBlocked == true;
    public bool IsNewLimitBlocked => Eligibility?.IsNewLimitBlocked == true;
    public AsyncCommand AgainCommand { get; }
    public AsyncCommand HardCommand { get; }
    public AsyncCommand GoodCommand { get; }
    public AsyncCommand EasyCommand { get; }
    public AsyncCommand StarCommand { get; }
    public RelayCommand EditCommand { get; }
    internal PracticeTargetViewModel(VocabularyItem word, PracticeUsage usage,
        Func<PracticeTargetViewModel, ReviewRating?, AsyncCommand> command, Action<VocabularyItem> edit)
    {
        Word = word; Usage = usage;
        AgainCommand = command(this, ReviewRating.Again); HardCommand = command(this, ReviewRating.Hard);
        GoodCommand = command(this, ReviewRating.Good); EasyCommand = command(this, ReviewRating.Easy);
        StarCommand = command(this, null); EditCommand = new(_ => edit(Word));
    }
    internal AsyncCommand[] Commands => [AgainCommand, HardCommand, GoodCommand, EasyCommand, StarCommand];
    internal void NotifyCommands()
    { OnPropertyChanged(nameof(CanOfferRating)); foreach (var command in Commands) command.NotifyCanExecuteChanged(); }
}

public sealed class PracticeViewModel : PageViewModel
{
    private readonly IStudyStore study;
    private readonly IPracticeStore store;
    private readonly IPracticeGenerator generator;
    private readonly IPracticeSpeech speech;
    private readonly AppSettings settings;
    private readonly Action saveSettings;
    private readonly Action<VocabularyItem?> edit;
    private readonly Func<bool> confirmReplace;
    private readonly TimeProvider clock;
    private readonly PracticeSelector selector = new();
    private readonly Queue<Guid[]> recentWords = new();
    private PracticeRequest? request;
    private PracticeMaterial? material;
    private PracticeCompletion? completion;
    private Guid exerciseId;
    private Guid? lastRating;
    private bool submitted;
    private bool choosingOptions;
    private int questionCount;
    private PracticeMode mode;
    public CategorySelection Topics { get; } = new("幫我選");
    public PracticeChoices<PracticeLevel> Difficulty { get; }
    public PracticeChoices<PracticeLength> Length { get; }
    public PracticeChoices<WordDensity> Density { get; }
    public PracticeChoices<PassageKind> Kind { get; private set; } = null!;
    public int[] QuestionCounts { get; } = [3, 4, 5];
    public int QuestionCount { get => questionCount; set => SetProperty(ref questionCount, value); }
    public int SpeechRate { get; set; }
    public bool IsReading => mode == PracticeMode.Reading;
    public bool IsListening => !IsReading;
    public bool HasMaterial => material is not null;
    public bool IsChoosingOptions => !HasMaterial || choosingOptions;
    public string OptionsActionText => IsChoosingOptions ? "返回當次練習" : "重新設定練習";
    public bool IsSubmitted => submitted;
    public bool IsUnsubmitted => HasMaterial && !submitted;
    public bool ShowPassage => HasMaterial && (request!.Options.Mode == PracticeMode.Reading || submitted);
    public bool ShowPlayer => HasMaterial && request!.Options.Mode == PracticeMode.Listening;
    public string Title => material?.Title ?? "";
    public string Passage => ShowPassage ? material!.Passage : "";
    public string Translation => submitted ? material!.Translation : "";
    public string Score => !submitted ? "" : $"完成了！{Questions.Count(x => x.IsCorrect)} / {Questions.Length} 題答對";
    public string ExerciseLabel => request is null ? "" : string.Join(" · ", request.Options.Topics);
    public string PlaybackText => !speech.IsAvailable ? "沒有 Windows 英文語音，請先在 Windows 設定安裝英文語音。" : speech.Playback switch
    { PlaybackState.Playing => "正在播放", PlaybackState.Paused => "已暫停", _ => "可重複聽，也可以先看題目。提交後才顯示逐字稿。" };
    public PracticeQuestionViewModel[] Questions { get; private set; } = [];
    public PracticeTargetViewModel[] Targets { get; private set; } = [];
    public PracticeTopicActivity[] TopicActivities { get; private set; } = [];
    public int CompletedCount { get; private set; }
    public string HistoryTitle => $"最近 {ApplicationConfiguration.Current.Practice.HistoryMonths} 個月 · 主題練習足跡";
    public string HistoryHelp => $"只保留最近 {ApplicationConfiguration.Current.Practice.HistoryMonths} 個月的完成時間、主題與目標詞義，協助安排下次練習。文章、答案和分數不存入歷史紀錄。";
    public string HistorySummary { get; private set; } = $"最近 {ApplicationConfiguration.Current.Practice.HistoryMonths} 個月尚無完成的練習";
    public AsyncCommand GenerateCommand { get; }
    public AsyncCommand SubmitCommand { get; }
    public AsyncCommand UndoCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand ReadingCommand { get; }
    public RelayCommand ListeningCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand AddWordCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand ToggleOptionsCommand { get; }

    public PracticeViewModel(IStudyStore study, IPracticeStore store, IPracticeGenerator generator,
        IPracticeSpeech speech, AppSettings settings, Action saveSettings, Action<VocabularyItem?> edit,
        Func<bool>? confirmReplace = null, TimeProvider? clock = null, Action? openSettings = null)
    {
        this.study = study; this.store = store; this.generator = generator; this.speech = speech;
        this.settings = settings; this.saveSettings = saveSettings; this.edit = edit; this.clock = clock ?? TimeProvider.System;
        this.confirmReplace = confirmReplace ?? (() => Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.ReplacePractice));
        var options = settings.Practice; mode = options.Mode; questionCount = options.QuestionCount; SpeechRate = options.SpeechRate;
        Difficulty = new([(PracticeLevel.Easy, "輕鬆"), (PracticeLevel.Medium, "適中"), (PracticeLevel.Hard, "挑戰")], options.Level);
        string LengthLabel(PracticeLength length, string label)
        {
            var range = CodexContentGenerator.WordRange(length);
            return $"{label} · {range.Minimum}–{range.Maximum} 字";
        }
        Length = new([(PracticeLength.Short, LengthLabel(PracticeLength.Short, "短篇")), (PracticeLength.Medium, LengthLabel(PracticeLength.Medium, "中篇")), (PracticeLength.Long, LengthLabel(PracticeLength.Long, "長篇"))], options.Length);
        Density = new([(WordDensity.Low, "少量"), (WordDensity.Medium, "適中"), (WordDensity.High, "較多")], options.Density);
        SetKinds(options.Kind);
        GenerateCommand = Command(GenerateAsync);
        SubmitCommand = Command(SubmitAsync, () => IsUnsubmitted && Questions.All(x => x.SelectedIndex >= 0));
        RefreshCommand = Command(LoadAsync);
        UndoCommand = Command(async token => { await study.UndoReviewAsync(lastRating!.Value, token); lastRating = null; await RefreshEligibility(token); }, () => lastRating.HasValue);
        ReadingCommand = new(_ => ChangeMode(PracticeMode.Reading), _ => !IsBusy);
        ListeningCommand = new(_ => ChangeMode(PracticeMode.Listening), _ => !IsBusy);
        CancelCommand = new(_ => GenerateCommand.Cancel());
        PlayCommand = new(_ => SpeechAction(() => speech.Play(material!.Passage, SpeechRate)), _ => ShowPlayer && speech.IsAvailable);
        PauseCommand = new(_ => SpeechAction(speech.Pause), _ => speech.Playback == PlaybackState.Playing);
        ResumeCommand = new(_ => SpeechAction(speech.Resume), _ => speech.Playback == PlaybackState.Paused);
        StopCommand = new(_ => SpeechAction(speech.Stop));
        AddWordCommand = new(_ => edit(null), _ => !IsBusy);
        OpenSettingsCommand = new(_ => openSettings?.Invoke(), _ => !IsBusy && openSettings is not null);
        ToggleOptionsCommand = new(_ => { choosingOptions = !choosingOptions; NotifyState(); }, _ => HasMaterial && !IsBusy);
        var context = SynchronizationContext.Current;
        speech.PlaybackChanged += () => { if (context is null) NotifySpeech(); else context.Post(_ => NotifySpeech(), null); };
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(IsBusy)) return;
            ReadingCommand.NotifyCanExecuteChanged(); ListeningCommand.NotifyCanExecuteChanged(); AddWordCommand.NotifyCanExecuteChanged(); OpenSettingsCommand.NotifyCanExecuteChanged(); ToggleOptionsCommand.NotifyCanExecuteChanged();
        };
    }

    private void SetKinds(PassageKind selected) => Kind = new(IsReading
        ? [(PassageKind.Random, "幫我選"), (PassageKind.Email, "信件"), (PassageKind.Notice, "公告"), (PassageKind.Advertisement, "廣告"), (PassageKind.Business, "職場情境"), (PassageKind.Story, "有趣的文章")]
        : [(PassageKind.Random, "幫我選"), (PassageKind.Dialogue, "對話"), (PassageKind.Monologue, "獨白")], selected);
    private void ChangeMode(PracticeMode value)
    {
        if (mode == value || IsUnsubmitted && !confirmReplace()) return;
        StopSpeech(); mode = value; ClearExercise(); SetKinds(PassageKind.Random); NotifyState();
    }
    private void ClearExercise()
    {
        foreach (var command in Targets.SelectMany(x => x.Commands)) ForgetCommand(command);
        material = null; request = null; completion = null; Questions = []; Targets = []; submitted = false; choosingOptions = false; lastRating = null;
    }
    public void Reset() { StopSpeech(); ClearExercise(); recentWords.Clear(); NotifyState(); }
    public void StopSpeech() => speech.Stop();
    private void SpeechAction(Action action) { try { action(); } catch (Exception ex) { Error = ex.Message; } }
    private void NotifySpeech()
    { OnPropertyChanged(nameof(PlaybackText)); PlayCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged(); ResumeCommand.NotifyCanExecuteChanged(); }
    private void NotifyState()
    {
        OnPropertyChanged(""); SubmitCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged(); ToggleOptionsCommand.NotifyCanExecuteChanged(); NotifySpeech();
    }
    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var firstLoad = Topics.Choices.Count == 1;
        var candidates = await store.GetPracticeCandidatesAsync(cancellationToken);
        Topics.SetAvailable(candidates.SelectMany(x => x.Word.Categories).Order());
        if (firstLoad) Topics.SetSelected(settings.Practice.Topics);
        await RefreshHistory(cancellationToken);
        if (submitted) await RefreshEligibility(cancellationToken);
    }
    private async Task RefreshHistory(CancellationToken token)
    {
        var now = clock.GetLocalNow();
        var cutoff = new DateTimeOffset(now.Date.AddMonths(-ApplicationConfiguration.Current.Practice.HistoryMonths), now.Offset);
        var history = (await store.GetPracticeHistoryAsync(now, token)).Where(x => x.CompletedAt >= cutoff && x.CompletedAt <= now).ToArray();
        var availableTopics = await study.GetCategoriesAsync(token);
        TopicActivities = PracticeTopicActivity.Build(availableTopics, history);
        CompletedCount = history.Length;
        HistorySummary = $"最近 {ApplicationConfiguration.Current.Practice.HistoryMonths} 個月 · 共 {CompletedCount} 次 · 閱讀 {history.Count(x => x.Mode == PracticeMode.Reading)} 次 · 聽力 {history.Count(x => x.Mode == PracticeMode.Listening)} 次";
        OnPropertyChanged(nameof(TopicActivities)); OnPropertyChanged(nameof(CompletedCount)); OnPropertyChanged(nameof(HistorySummary));
    }
    private async Task GenerateAsync(CancellationToken token)
    {
        if (IsUnsubmitted && !confirmReplace()) return;
        var options = new PracticeOptions { Mode = mode, Kind = Kind.Value, Level = Difficulty.Value, Length = Length.Value,
            Density = Density.Value, QuestionCount = QuestionCount, Topics = Topics.SelectedNames, SpeechRate = SpeechRate };
        options.Validate();
        var candidates = await store.GetPracticeCandidatesAsync(token);
        var history = await store.GetPracticeHistoryAsync(clock.GetUtcNow(), token);
        var nextRequest = selector.Select(options, candidates, history, clock.GetUtcNow(), recentWords.SelectMany(x => x).ToHashSet());
        Notice = "正在生成文章與題目，通常需要一點時間。可取消並保留當次內容。";
        var next = await generator.GeneratePracticeAsync(nextRequest, token);
        token.ThrowIfCancellationRequested();
        settings.Practice = options; saveSettings();
        StopSpeech(); ClearExercise(); request = nextRequest; material = next; exerciseId = Guid.NewGuid();
        Questions = next.Questions.Select((x, i) => new PracticeQuestionViewModel(x, i + 1)).ToArray();
        foreach (var question in Questions) question.Changed += () => SubmitCommand.NotifyCanExecuteChanged();
        recentWords.Enqueue(nextRequest.Targets.Select(x => x.Id).ToArray());
        while (recentWords.Count > ApplicationConfiguration.Current.Practice.RecentSessionCount) recentWords.Dequeue();
        Notice = ""; NotifyState();
    }
    private async Task SubmitAsync(CancellationToken token)
    {
        StopSpeech();
        completion ??= new(exerciseId, clock.GetUtcNow(), request!.Options.Mode, request.Options.Topics, request.Targets.Select(x => x.Id).ToArray());
        foreach (var question in Questions) question.Lock();
        await store.CompletePracticeAsync(completion, token);
        submitted = true;
        foreach (var question in Questions) question.Reveal();
        Targets = material!.Targets.Select(x => new PracticeTargetViewModel(request!.Targets.Single(w => w.Id == x.SenseId), x, TargetCommand,
            word => { if (!IsBusy) edit(word); })).ToArray();
        NotifyState();
        await RefreshEligibility(token); await RefreshHistory(token);
    }
    private AsyncCommand TargetCommand(PracticeTargetViewModel target, ReviewRating? rating) => Command(async token =>
    {
        await RefreshEligibility(token);
        if (rating is null)
            await study.SetStarredAsync(target.Word.Id, target.Eligibility?.IsStarred != true, token);
        else
        {
            if (target.Pending is not null && target.Pending.Rating != rating)
                throw new InvalidOperationException("上次評分尚未確認，請先重試相同評分。");
            if (target.Pending is null && target.Eligibility?.CanRate != true)
                throw new InvalidOperationException(target.Status);
            target.Pending ??= new(target.Word.Id, rating.Value, clock.GetUtcNow(), Guid.NewGuid(), target.Eligibility!.Version);
            try
            {
                var result = await store.RatePracticeAsync(exerciseId, target.Pending, settings.DailyNewLimit, token);
                lastRating = result.OperationId; target.Pending = null;
                Notice = $"已更新排程。下一次複習：{result.DueAt.ToLocalTime():MM/dd HH:mm}";
            }
            catch
            {
                // Resolve ambiguous commits before a later retry; never silently change an operation's rating/time.
                await RefreshEligibility(CancellationToken.None);
                if (target.Eligibility?.AppliedOperation == target.Pending!.OperationId) { lastRating = target.Pending.OperationId; target.Pending = null; }
                else if (target.Eligibility?.Version != target.Pending.ExpectedScheduleVersion || target.Eligibility?.CanRate != true) target.Pending = null;
                throw;
            }
        }
        await RefreshEligibility(token); UndoCommand.NotifyCanExecuteChanged();
    }, () => submitted && (rating is null || (target.Pending is { } pending ? pending.Rating == rating : target.Eligibility?.CanRate == true)));
    private async Task RefreshEligibility(CancellationToken token)
    {
        var states = await store.GetPracticeEligibilityAsync(exerciseId, clock.GetUtcNow(), settings.DailyNewLimit, token);
        var words = await study.GetVocabularyAsync(cancellationToken: token, includeArchived: true);
        foreach (var target in Targets)
        {
            if (words.FirstOrDefault(x => x.Id == target.Word.Id) is { } word) target.UpdateWord(word);
            target.Eligibility = states.FirstOrDefault(x => x.SenseId == target.Word.Id)
                ?? new(target.Word.Id, 0, false, "此詞義或練習已不在資料庫中", target.Word.IsStarred, null);
            target.NotifyCommands();
        }
    }
}
