namespace Wording.Core;

public enum TranslationVerdict { Correct, PartlyCorrect, NeedsWork }

public sealed record TranslationRequest(VocabularyItem[] Targets);
public sealed record TranslationQuestion(Guid SenseId, string Chinese, string ReferenceEnglish);
public sealed record TranslationMaterial(TranslationQuestion[] Questions);
public sealed record TranslationAnswer(Guid SenseId, string English);
public sealed record TranslationSubmission(TranslationRequest Request, TranslationMaterial Material,
    TranslationAnswer[] Answers);
public sealed record TranslationFeedback(Guid SenseId, TranslationVerdict Verdict, string MeaningFeedback,
    string TargetFeedback, string GrammarFeedback, string SuggestedEnglish);
public sealed record TranslationAssessment(TranslationFeedback[] Feedback);

// This contract has no review-rating or persistence operation. Translation results never update FSRS.
public interface ITranslationGenerator
{
    Task<TranslationMaterial> GenerateTranslationAsync(TranslationRequest request, CancellationToken token = default);
    Task<TranslationAssessment> AssessTranslationAsync(TranslationSubmission submission, CancellationToken token = default);
}
