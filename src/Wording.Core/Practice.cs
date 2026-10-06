using System.Numerics;

namespace Wording.Core;

public enum PracticeMode { Reading, Listening }
public enum PassageKind { Random, Email, Notice, Advertisement, Business, Story, Dialogue, Monologue }
public enum PracticeLevel { Easy, Medium, Hard }
public enum PracticeLength { Short, Medium, Long }
public enum WordDensity { Low, Medium, High }
public enum PlaybackState { Stopped, Playing, Paused }

public sealed record PracticeOptions
{
    public PracticeMode Mode { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.Mode;
    public PassageKind Kind { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.Kind;
    public PracticeLevel Level { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.Level;
    public PracticeLength Length { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.Length;
    public WordDensity Density { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.Density;
    public int QuestionCount { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.QuestionCount;
    public string[] Topics { get; init; } = [];
    public int SpeechRate { get; init; } = ApplicationConfiguration.Current.Practice.Defaults.SpeechRate;

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Kind) || !Enum.IsDefined(Level) ||
            !Enum.IsDefined(Length) || !Enum.IsDefined(Density) || QuestionCount is < 3 or > 5 ||
            SpeechRate is < -2 or > 2 || Topics is null || Topics.Length > 30 ||
            Topics.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
            throw new ArgumentException("練習選項無效。");
        if (Mode == PracticeMode.Reading && Kind is PassageKind.Dialogue or PassageKind.Monologue ||
            Mode == PracticeMode.Listening && Kind is not (PassageKind.Random or PassageKind.Dialogue or PassageKind.Monologue))
            throw new ArgumentException("文章類型不適用目前模式。");
    }
}

public sealed record PracticeCandidate(VocabularyItem Word, ReviewSchedule Schedule);
public sealed record PracticeRequest(PracticeOptions Options, VocabularyItem[] Targets);
public sealed record PracticeQuestion(string Kind, string Prompt, string[] Options, int AnswerIndex,
    string Explanation, string Evidence, string[] OptionExplanations);
public sealed record PracticeUsage(Guid SenseId, string Surface, string Meaning, string Evidence);
public sealed record PracticeMaterial(string Title, string Passage, string Translation,
    PracticeQuestion[] Questions, PracticeUsage[] Targets);
// Only completion metadata is persisted. Material and answers remain in the view model.
public sealed record PracticeCompletion(Guid Id, DateTimeOffset CompletedAt, PracticeMode Mode,
    string[] Topics, Guid[] TargetIds);
public sealed record PracticeEligibility(Guid SenseId, long Version, bool CanRate, string Message,
    bool IsStarred, Guid? AppliedOperation);

public interface IPracticeStore
{
    Task<IReadOnlyList<PracticeCandidate>> GetPracticeCandidatesAsync(CancellationToken token = default);
    Task<IReadOnlyList<PracticeCompletion>> GetPracticeHistoryAsync(DateTimeOffset now, CancellationToken token = default);
    Task CompletePracticeAsync(PracticeCompletion completion, CancellationToken token = default);
    Task<IReadOnlyList<PracticeEligibility>> GetPracticeEligibilityAsync(Guid exerciseId,
        DateTimeOffset now, BigInteger dailyLimit, CancellationToken token = default);
    Task<ReviewResult> RatePracticeAsync(Guid exerciseId, ReviewSubmission submission,
        BigInteger dailyLimit, CancellationToken token = default);
}
public interface IPracticeGenerator
{
    Task<PracticeMaterial> GeneratePracticeAsync(PracticeRequest request, CancellationToken token = default);
}
public interface IPracticeSpeech
{
    bool IsAvailable { get; }
    PlaybackState Playback { get; }
    event Action? PlaybackChanged;
    void Play(string text, int rate);
    void Pause();
    void Resume();
    void Stop();
}
