using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop.ViewModels;

public sealed class TranslationQuestionViewModel(int number, VocabularyItem target, TranslationQuestion question) : ObservableObject
{
    private string answer = "";
    private TranslationFeedback? feedback;
    public event Action? Changed;
    public Guid SenseId => target.Id;
    public string Number => $"第 {number} 題";
    public string Target => target.Headword;
    public string TargetMeaning => $"{PartOfSpeechDisplay.Chinese(target.PartOfSpeech)} · {target.Meaning}";
    public string Chinese => question.Chinese;
    public string Answer
    {
        get => answer;
        set
        {
            if (!SetProperty(ref answer, value)) return;
            feedback = null;
            NotifyFeedback();
            Changed?.Invoke();
        }
    }
    public bool HasFeedback => feedback is not null;
    public string VerdictText => feedback?.Verdict switch
    {
        TranslationVerdict.Correct => "正確", TranslationVerdict.PartlyCorrect => "部分正確",
        TranslationVerdict.NeedsWork => "需要修改", _ => ""
    };
    public string VerdictColor => feedback?.Verdict switch
    { TranslationVerdict.Correct => "#047857", TranslationVerdict.PartlyCorrect => "#B45309", _ => "#BE123C" };
    public string MeaningFeedback => feedback?.MeaningFeedback ?? "";
    public string TargetFeedback => feedback?.TargetFeedback ?? "";
    public string GrammarFeedback => feedback?.GrammarFeedback ?? "";
    public string SuggestedEnglish => feedback?.SuggestedEnglish ?? "";
    public string ReferenceEnglish => HasFeedback ? question.ReferenceEnglish : "";

    public void Apply(TranslationFeedback value)
    {
        feedback = value;
        NotifyFeedback();
        Changed?.Invoke();
    }
    private void NotifyFeedback()
    {
        foreach (var name in new[] { nameof(HasFeedback), nameof(VerdictText), nameof(VerdictColor), nameof(MeaningFeedback),
                     nameof(TargetFeedback), nameof(GrammarFeedback), nameof(SuggestedEnglish), nameof(ReferenceEnglish) })
            OnPropertyChanged(name);
    }
}

public sealed class TranslationViewModel : PageViewModel
{
    private readonly IPracticeStore store;
    private readonly ITranslationGenerator generator;
    private readonly TranslationSelector selector;
    private readonly TimeProvider clock;
    private readonly Func<bool> confirmReplace;
    private readonly Queue<Guid[]> recentWords = new();
    private TranslationRequest? request;
    private TranslationMaterial? material;
    private int questionCount = 3;
    private bool hasSubmitted;
    public int[] QuestionCounts { get; } = [3, 5];
    public int QuestionCount
    {
        get => questionCount;
        set { if (QuestionCounts.Contains(value)) SetProperty(ref questionCount, value); }
    }
    public TranslationQuestionViewModel[] Questions { get; private set; } = [];
    public bool HasMaterial => Questions.Length > 0;
    public bool ShowEmptyState => !HasMaterial;
    public bool IsUnsubmitted => HasMaterial && Questions.Any(x => !x.HasFeedback);
    public string Summary => !HasMaterial ? "每題練習一個指定單字，請用它把中文句子寫成英文。" :
        Questions.All(x => x.HasFeedback) ? $"已完成 {Questions.Length} 題批改，可以修改答案後重新提交。" :
        $"本次 {Questions.Length} 題 · 已填寫 {Questions.Count(x => !string.IsNullOrWhiteSpace(x.Answer))} 題";
    public string SubmitText => hasSubmitted ? "重新批改" : "送出批改";
    public AsyncCommand GenerateCommand { get; }
    public AsyncCommand SubmitCommand { get; }
    public RelayCommand CancelCommand { get; }

    public TranslationViewModel(IPracticeStore store, ITranslationGenerator generator, Func<bool>? confirmReplace = null,
        TimeProvider? clock = null, TranslationSelector? selector = null)
    {
        this.store = store;
        this.generator = generator;
        this.confirmReplace = confirmReplace ?? (() => Views.ConfirmationDialog.Confirm(Views.ConfirmationContent.ReplaceTranslation));
        this.clock = clock ?? TimeProvider.System;
        this.selector = selector ?? new();
        GenerateCommand = Command(GenerateAsync);
        SubmitCommand = Command(SubmitAsync, () => HasMaterial && Questions.All(x => !string.IsNullOrWhiteSpace(x.Answer)) &&
            Questions.Any(x => !x.HasFeedback));
        CancelCommand = new(_ => { GenerateCommand.Cancel(); SubmitCommand.Cancel(); });
    }

    private async Task GenerateAsync(CancellationToken token)
    {
        if (IsUnsubmitted && !confirmReplace()) return;
        var candidates = await store.GetPracticeCandidatesAsync(token);
        var nextRequest = selector.Select(candidates, QuestionCount, clock.GetUtcNow(), recentWords.SelectMany(x => x).ToHashSet());
        var nextMaterial = await generator.GenerateTranslationAsync(nextRequest, token);
        token.ThrowIfCancellationRequested();
        foreach (var question in Questions) question.Changed -= NotifyState;
        request = nextRequest;
        material = nextMaterial;
        Questions = nextRequest.Targets.Select((target, i) => new TranslationQuestionViewModel(i + 1, target,
            nextMaterial.Questions.Single(x => x.SenseId == target.Id))).ToArray();
        foreach (var question in Questions) question.Changed += NotifyState;
        hasSubmitted = false;
        recentWords.Enqueue(nextRequest.Targets.Select(x => x.WordId).ToArray());
        while (recentWords.Count > 2) recentWords.Dequeue();
        Notice = nextRequest.Targets.Length < QuestionCount ? $"目前可用的較不熟悉單字較少，本次提供 {Questions.Length} 題。" : "";
        NotifyState();
    }

    private async Task SubmitAsync(CancellationToken token)
    {
        var answers = Questions.Select(x => new TranslationAnswer(x.SenseId, x.Answer.Trim())).ToArray();
        var assessment = await generator.AssessTranslationAsync(new(request!, material!, answers), token);
        token.ThrowIfCancellationRequested();
        // A late response must never grade text that was edited after this submission.
        if (!Questions.Select(x => x.Answer.Trim()).SequenceEqual(answers.Select(x => x.English)))
            throw new InvalidOperationException("答案已修改，請重新提交目前的內容。");
        foreach (var question in Questions) question.Apply(assessment.Feedback.Single(x => x.SenseId == question.SenseId));
        hasSubmitted = true;
        Notice = "批改完成。AI 建議僅供練習，作答不影響單字熟悉度與複習排程。";
        NotifyState();
    }

    public void Reset()
    {
        foreach (var question in Questions) question.Changed -= NotifyState;
        Questions = []; request = null; material = null; recentWords.Clear(); hasSubmitted = false;
        Error = ""; Notice = "";
        NotifyState();
    }

    private void NotifyState()
    {
        foreach (var name in new[] { nameof(Questions), nameof(HasMaterial), nameof(ShowEmptyState), nameof(IsUnsubmitted), nameof(Summary), nameof(SubmitText) })
            OnPropertyChanged(name);
        SubmitCommand.NotifyCanExecuteChanged();
    }
}
