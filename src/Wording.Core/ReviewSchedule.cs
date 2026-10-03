namespace Wording.Core;

// 到期、暫停與畫面正反面各自獨立；只有這三個階段由 FSRS 管理。
public enum LearningState { Learning = 1, Review = 2, Relearning = 3 }

public sealed record ReviewSchedule(Guid CardId, LearningState State, int? Step,
    double? Stability, double? Difficulty, DateTimeOffset DueAt,
    DateTimeOffset? LastReviewAt);

public sealed class ReviewConflictException(string message) : InvalidOperationException(message);

public sealed class StudyDataException(string message) : InvalidOperationException(message);
