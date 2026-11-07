using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public enum ReadingPhase { Offline, Connecting, Ready, Starting, Recording, Finishing, Failed }

// One connection per review page; each example owns only its displayed result.
public sealed class ReadingPracticeViewModel : ObservableObject
{
    private readonly Action stopSpeech;
    private readonly AppSettings shortcuts;
    private ReadingPhase phase;
    private string status = "正在準備語音連線…";
    private bool answerVisible, hasRecording;
    private bool connectionInterrupted;
    private ReviewExampleViewModel? selected;
    private IReadOnlyList<ReviewExampleViewModel> examples = [];
    public AiSettings Settings { get; }
    public string Directory { get; }
    public int Run { get; private set; }
    public event Action<string, int>? Requested;
    public ReadingPhase Phase => phase;
    public string Status => connectionInterrupted ? "連線暫時不穩，正在等待恢復，最多 15 秒…" : status;
    public bool IsAvailable => answerVisible && selected is not null;
    public bool HasError => phase == ReadingPhase.Failed;
    public string StateLabel => connectionInterrupted ? "等待恢復" : phase switch
    {
        ReadingPhase.Connecting => "連線中", ReadingPhase.Ready => hasRecording ? "比對完成" : "可錄音",
        ReadingPhase.Starting => "開啟麥克風", ReadingPhase.Recording => "收音中", ReadingPhase.Finishing => "處理中",
        ReadingPhase.Failed => "需要處理", _ => "未連線"
    };
    public string StateColor => connectionInterrupted ? "#985300" : phase switch
    {
        ReadingPhase.Ready => "#167548", ReadingPhase.Recording or ReadingPhase.Failed => "#B42335",
        ReadingPhase.Connecting or ReadingPhase.Starting => "#985300", ReadingPhase.Finishing => "#4F46E5", _ => "#65738A"
    };
    public string StateBackground => connectionInterrupted ? "#FFF6E5" : phase switch
    {
        ReadingPhase.Ready => "#EAF6EF", ReadingPhase.Recording or ReadingPhase.Failed => "#FFF1F0",
        ReadingPhase.Connecting or ReadingPhase.Starting => "#FFF6E5", ReadingPhase.Finishing => "#EEEDFF", _ => "#F0F2F6"
    };
    private static string KeyLabel(string key) => key == "Space" ? "空白鍵" : key;
    public string RecordLabel => $"{(phase == ReadingPhase.Recording ? "結束並比對" : phase == ReadingPhase.Failed ? "重新連線" : "開始朗讀")} · {KeyLabel(shortcuts.RecordReadingKey)}";
    public string ReplayLabel => $"回聽 · {KeyLabel(shortcuts.ReplayReadingKey)}";
    public string NextLabel => $"下一句 · {KeyLabel(shortcuts.NextReadingKey)} ↻";
    public RelayCommand PronounceCommand { get; }
    public RelayCommand RecordCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ReplayCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }

    public ReadingPracticeViewModel(AiSettings settings, string directory, Action stopSpeech, AppSettings shortcuts, IPronunciationService? pronunciation = null, Action? openSettings = null)
    {
        this.shortcuts = shortcuts;
        Settings = settings; Directory = directory; this.stopSpeech = stopSpeech;
        OpenSettingsCommand = new(_ => openSettings!(), _ => HasError && openSettings is not null);
        PronounceCommand = new(value =>
        {
            if (CancelCommand?.CanExecute(null) == true) CancelCommand.Execute(null);
            PauseReplay();
            pronunciation!.Speak((string)value!);
        }, value => pronunciation is not null && IsAvailable && value is string { Length: > 0 });
        RecordCommand = new(_ =>
        {
            if (phase == ReadingPhase.Failed) { SetState(ReadingPhase.Connecting, "正在重新準備語音連線…"); Requested?.Invoke("warm", ++Run); }
            else if (phase == ReadingPhase.Recording) { SetState(ReadingPhase.Finishing, "正在收尾辨識並比對文字…"); Requested?.Invoke("finish", Run); }
            else
            {
                stopSpeech(); selected!.Clear(); hasRecording = false;
                SetState(ReadingPhase.Starting, "正在開啟麥克風…"); Requested?.Invoke("record", ++Run);
            }
        }, _ => IsAvailable && (phase == ReadingPhase.Recording || !connectionInterrupted && phase is ReadingPhase.Ready or ReadingPhase.Failed));
        CancelCommand = new(_ => Cancel(), _ => phase is ReadingPhase.Starting or ReadingPhase.Recording or ReadingPhase.Finishing);
        ReplayCommand = new(_ => { stopSpeech(); Requested?.Invoke("replay", Run); }, _ => IsAvailable && hasRecording && phase == ReadingPhase.Ready);
        SelectCommand = new(value => { if (value is ReviewExampleViewModel example) Select(example); }, value => answerVisible && value is ReviewExampleViewModel example && examples.Contains(example));
        PreviousCommand = new(_ => Move(-1), _ => IsAvailable && examples.Count > 1);
        NextCommand = new(_ => Move(1), _ => IsAvailable && examples.Count > 1);
    }

    public void SetExamples(IReadOnlyList<ReviewExampleViewModel> value)
    {
        ResetCapture(); examples = value; selected = examples.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.English));
        foreach (var example in examples) example.RefreshSelection();
        Notify();
    }
    public void SetAnswerVisible(bool value) { answerVisible = value; if (!value) ResetCapture(); Notify(); }
    public bool IsSelected(ReviewExampleViewModel example) => selected == example;
    private void Select(ReviewExampleViewModel example)
    {
        if (selected == example || string.IsNullOrWhiteSpace(example.English)) return;
        ResetCapture(); var previous = selected; selected = example; previous?.RefreshSelection(); selected.RefreshSelection(); Notify();
    }
    private void Move(int direction)
    {
        var index = examples.ToList().IndexOf(selected!);
        for (var offset = 1; offset <= examples.Count; offset++)
        {
            var candidate = examples[(index + direction * offset + examples.Count) % examples.Count];
            if (!string.IsNullOrWhiteSpace(candidate.English)) { Select(candidate); break; }
        }
    }
    private void ResetCapture()
    {
        hasRecording = false;
        if (phase is ReadingPhase.Starting or ReadingPhase.Recording or ReadingPhase.Finishing) Cancel();
        else Requested?.Invoke("reset", ++Run);
        Notify();
    }
    private void Cancel()
    {
        selected?.Clear(); hasRecording = false;
        SetState(ReadingPhase.Finishing, "已取消，正在準備下一次朗讀…"); Requested?.Invoke("cancel", ++Run);
    }
    public void PauseReplay() => Requested?.Invoke("pause-playback", Run);
    public void Connecting(string? message = null) { ++Run; hasRecording = false; connectionInterrupted = false; SetState(ReadingPhase.Connecting, message ?? "正在背景準備語音連線，可繼續複習…"); }
    public void ConnectionInterrupted(bool value) { connectionInterrupted = value; Notify(); }
    public void Ready() => SetState(ReadingPhase.Ready, "語音已就緒 · 按開始朗讀才會開啟麥克風");
    public void Recording(int run) { if (run == Run) SetState(ReadingPhase.Recording, $"正在收音 · 讀完按{KeyLabel(shortcuts.RecordReadingKey)}，最多 30 秒"); }
    public void Transcript(int run, string text) { if (run == Run && phase is ReadingPhase.Recording or ReadingPhase.Finishing) selected?.Update(text); }
    public void Complete(int run, string text)
    {
        if (run != Run || phase != ReadingPhase.Finishing || selected is null) return;
        selected.Update(text, ReadingComparison.Compare(selected.English, text)); hasRecording = true;
        SetState(ReadingPhase.Ready, "比對完成 · 這次練習不影響單字熟悉度");
    }
    public void Cancelled(int run) { if (run == Run) Ready(); }
    public void Fail(string message) { ++Run; hasRecording = false; connectionInterrupted = false; SetState(ReadingPhase.Failed, message); }
    public void Disconnected() { ++Run; hasRecording = false; connectionInterrupted = false; SetState(ReadingPhase.Offline, "語音連線已關閉"); }
    private void SetState(ReadingPhase value, string text) { phase = value; status = text; Notify(); }
    private void Notify()
    {
        OnPropertyChanged(nameof(Phase)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(RecordLabel)); OnPropertyChanged(nameof(ReplayLabel)); OnPropertyChanged(nameof(NextLabel)); OnPropertyChanged(nameof(IsAvailable));
        foreach (var name in new[] { nameof(HasError), nameof(StateLabel), nameof(StateColor), nameof(StateBackground) }) OnPropertyChanged(name);
        OpenSettingsCommand?.NotifyCanExecuteChanged();
        RecordCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); ReplayCommand?.NotifyCanExecuteChanged();
        PronounceCommand?.NotifyCanExecuteChanged();
        SelectCommand?.NotifyCanExecuteChanged(); PreviousCommand?.NotifyCanExecuteChanged(); NextCommand?.NotifyCanExecuteChanged();
    }
}

public sealed class ReviewExampleViewModel(ExampleSentence sentence, ReadingPracticeViewModel? reading) : ObservableObject
{
    private string transcript = "";
    private ReadingComparison? comparison;
    public string English => sentence.English;
    public string Chinese => sentence.Chinese;
    public ReadingPracticeViewModel? Reading => reading;
    public bool CanRead => reading is not null && !string.IsNullOrWhiteSpace(English);
    public bool IsSelected => reading?.IsSelected(this) == true;
    public bool IsNotSelected => !IsSelected;
    public string Transcript => transcript;
    public bool HasTranscript => transcript.Length > 0;
    public bool HasResult => comparison is not null;
    public string Verdict => comparison is null ? "" : comparison.IsMatch ? "辨識內容與例句一致" : "有幾個字不同，可以再試一次";
    public string Counts => comparison is null ? "" : $"吻合 {comparison.Matched} · 不同 {comparison.Different} · 漏字 {comparison.Missing} · 多字 {comparison.Extra}";
    public IReadOnlyList<ReadingWordViewModel> Words => comparison?.Words.Select(x => new ReadingWordViewModel(x)).ToArray() ?? [];
    public void RefreshSelection() { OnPropertyChanged(nameof(IsSelected)); OnPropertyChanged(nameof(IsNotSelected)); }
    public void Clear() => Update("");
    public void Update(string text, ReadingComparison? result = null)
    {
        transcript = text; comparison = result;
        foreach (var property in new[] { nameof(Transcript), nameof(HasTranscript), nameof(HasResult), nameof(Verdict), nameof(Counts), nameof(Words) }) OnPropertyChanged(property);
    }
}

public sealed record ReadingWordViewModel(ReadingDifference Difference)
{
    public string Text => Difference.Kind switch { ReadingDifferenceKind.Different => $"{Difference.Expected} → {Difference.Heard}", ReadingDifferenceKind.Missing => $"漏：{Difference.Expected}", ReadingDifferenceKind.Extra => $"多：{Difference.Heard}", _ => Difference.Expected };
    public string Color => Difference.Kind switch { ReadingDifferenceKind.Match => "#167548", ReadingDifferenceKind.Extra => "#985300", _ => "#B42335" };
}
