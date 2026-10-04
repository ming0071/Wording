namespace Wording.Core;

public enum Enrollment { Candidate, Selected, Skipped }
public enum ReviewRating { Again = 1, Hard = 2, Good = 3, Easy = 4 }

public sealed record ExampleSentence(string English, string Chinese, ContentOrigin? Origin = null);
public sealed record ContentOrigin(string Kind, string Note, string? Model = null,
    DateTimeOffset? GeneratedAt = null, string? PromptVersion = null);

public sealed record VocabularyItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WordId => WordIdentity.For(Headword);
    public string Headword { get; init; } = "";
    public string PartOfSpeech { get; init; } = "";
    public string Meaning { get; init; } = "";
    public string EnglishDefinition { get; init; } = "";
    public string Cue { get; init; } = "";
    public string Level { get; init; } = "優先";
    public string Kind { get; init; } = "word";
    public string[] Categories { get; init; } = [];
    public string[] Collocations { get; init; } = [];
    public string[] Synonyms { get; init; } = [];
    public string Notes { get; init; } = "";
    public ExampleSentence[] Examples { get; init; } = [];
    public VocabularyDefinition[] AdditionalSenses { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<VocabularyDefinition> Definitions => [VocabularyDefinition.From(this), .. AdditionalSenses];
    [System.Text.Json.Serialization.JsonIgnore]
    public string DefinitionCountText => AdditionalSenses.Length == 0 ? PartOfSpeech : $"{AdditionalSenses.Length + 1} 組解釋 · {PartOfSpeech}";
    public ContentOrigin Origin { get; init; } = new("user", "使用者編寫");
    public ContentOrigin? MeaningOrigin { get; init; }
    public ContentOrigin? CollocationsOrigin { get; init; }
    public Enrollment Enrollment { get; init; }
    public bool IsArchived { get; init; }
    public bool IsPaused { get; init; }
    public bool IsUserEdited { get; init; }
    public bool IsStarred { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public double Stability { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long CreationOrder { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string StarLabel => IsStarred ? "★" : "☆";
}

public sealed record VocabularyDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartOfSpeech { get; init; } = "";
    public string Meaning { get; init; } = "";
    public string EnglishDefinition { get; init; } = "";
    public string Cue { get; init; } = "";
    public string Level { get; init; } = "優先";
    public string[] Categories { get; init; } = [];
    public string[] Collocations { get; init; } = [];
    public string[] Synonyms { get; init; } = [];
    public string Notes { get; init; } = "";
    public ExampleSentence[] Examples { get; init; } = [];

    public static VocabularyDefinition From(VocabularyItem word) => new()
    {
        Id = word.Id, PartOfSpeech = word.PartOfSpeech, Meaning = word.Meaning,
        EnglishDefinition = word.EnglishDefinition, Cue = word.Cue, Level = word.Level,
        Categories = word.Categories, Collocations = word.Collocations, Synonyms = word.Synonyms,
        Notes = word.Notes, Examples = word.Examples
    };

    public VocabularyItem AsWord(string headword) => new()
    {
        Id = Id, Headword = headword, PartOfSpeech = PartOfSpeech, Meaning = Meaning,
        EnglishDefinition = EnglishDefinition, Cue = Cue, Level = Level, Categories = Categories,
        Collocations = Collocations, Synonyms = Synonyms, Notes = Notes, Examples = Examples
    };
}

public sealed record SeedPack(string PackId, int Version, VocabularyItem[] Items);
public sealed record DashboardSummary(int DueCount, int NewCount, int ReviewedToday,
    int StartedToday, int TotalCount, DateTimeOffset? NextDue);
public sealed record StudyActivity(DateOnly Day, int ReviewedCount);
public sealed record ReviewItem(VocabularyItem Word, long ScheduleVersion, bool IsNew,
    DateTimeOffset? DueAt);
public sealed record ReviewSubmission(Guid SenseId, ReviewRating Rating,
    DateTimeOffset ReviewedAt, Guid OperationId, long ExpectedScheduleVersion);
public sealed record ReviewResult(Guid OperationId, DateTimeOffset DueAt, string State);
public sealed record AiEnrichment(string Meaning, string[] Collocations,
    ExampleSentence[] Examples, ContentOrigin Origin)
{
    public string EnglishDefinition { get; init; } = "";
    public string[] Synonyms { get; init; } = [];
}

public interface IStudyStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ImportSeedPackAsync(SeedPack pack, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VocabularyItem>> GetVocabularyAsync(string? search = null,
        string? category = null, CancellationToken cancellationToken = default, bool includeArchived = false);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task AddCategoryAsync(string name, CancellationToken cancellationToken = default);
    Task SaveVocabularyAsync(VocabularyItem item, CancellationToken cancellationToken = default);
    // Explicit overrides are used by JSON imports; regular editing preserves stored stars.
    Task SaveVocabularyBatchAsync(IReadOnlyList<VocabularyItem> items, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<Guid, bool>? starOverrides = null);
    Task SetEnrollmentAsync(Guid senseId, Enrollment enrollment, CancellationToken cancellationToken = default);
    Task SetPausedAsync(Guid senseId, bool paused, CancellationToken cancellationToken = default);
    Task SetStarredAsync(Guid senseId, bool starred, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid senseId, CancellationToken cancellationToken = default);
    Task<DashboardSummary> GetDashboardAsync(DateTimeOffset now, CancellationToken cancellationToken = default,
        string? category = null, IReadOnlyList<string>? categories = null);
    Task<ReviewItem?> GetNextReviewAsync(DateTimeOffset now, System.Numerics.BigInteger dailyNewLimit,
        CancellationToken cancellationToken = default, string? category = null, IReadOnlyList<string>? categories = null);
    Task<IReadOnlyList<StudyActivity>> GetStudyActivityAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);
    Task<ReviewResult> SubmitReviewAsync(ReviewSubmission submission, CancellationToken cancellationToken = default);
    Task UndoReviewAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task EndReviewSessionAsync(CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task CreateBackupAsync(string destination, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string source, CancellationToken cancellationToken = default);
}

public interface IContentGenerator
{
    Task<string> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<AiEnrichment> GenerateAsync(VocabularyItem word, CancellationToken cancellationToken = default);
}

public sealed record CodexModel(string Model, string DisplayName, string Description, bool IsDefault)
{
    public IReadOnlyList<CodexServiceTier> ServiceTiers { get; init; } = [];
}

public sealed record CodexServiceTier(string Id, string Name, string Description)
{
    public static bool IsValidId(string? value) => value is not null && value.Length <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

public interface ICodexModelCatalog
{
    Task<IReadOnlyList<CodexModel>> ListModelsAsync(string executable, CancellationToken cancellationToken = default);
}

public interface IPronunciationService : IDisposable
{
    string Status { get; }
    void Speak(string text);
    void Stop();
}
