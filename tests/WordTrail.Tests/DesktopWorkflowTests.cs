using WordTrail.Core;
using WordTrail.Desktop.ViewModels;
using WordTrail.Infrastructure;

namespace WordTrail.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Fact]
    public async Task ArchiveFilterShowsRestoreActionAndKeepsPauseAndEnrollment()
    {
        var store = new RecordingStore();
        await store.SaveVocabularyAsync(new VocabularyItem
        {
            Headword = "invoice", PartOfSpeech = "noun", Meaning = "發票",
            Enrollment = Enrollment.Selected, IsPaused = true, IsArchived = true
        });
        var library = new LibraryViewModel(store, _ => { });
        await library.LoadAsync();
        Assert.Empty(library.Items);
        Assert.False(library.RestoreCommand.CanExecute(null));

        library.SelectedFilter = "全部";
        await library.LoadAsync();
        Assert.Empty(library.Items);
        library.SelectedFilter = "已封存";
        await library.LoadAsync();
        Assert.True(Assert.Single(library.Items).IsArchived);
        Assert.True(library.RestoreCommand.CanExecute(null));
        Assert.False(library.PauseCommand.CanExecute(null));
        Assert.False(library.ArchiveCommand.CanExecute(null));

        await library.RestoreCommand.ExecuteAsync();
        Assert.Empty(library.Items);
        Assert.False(store.Saved!.IsArchived);
        Assert.True(store.Saved.IsPaused);
        Assert.Equal(Enrollment.Selected, store.Saved.Enrollment);
        Assert.Contains("已還原", library.Notice);

        library.SelectedFilter = "已暫停";
        await library.LoadAsync();
        Assert.Single(library.Items);
        Assert.False(library.RestoreCommand.CanExecute(null));
        Assert.True(library.ArchiveCommand.CanExecute(null));
    }

    [Fact]
    public async Task WordOnlyAiRequestRequiresExplicitApplyAndSave()
    {
        var store = new RecordingStore();
        var generator = new FixedGenerator();
        var editor = new EditorViewModel(store, generator, null, _ => { })
        { Headword = "invoice", AiConsent = true };

        await editor.GenerateCommand.ExecuteAsync();
        Assert.Equal("", editor.Error);
        Assert.Equal("invoice", generator.Request!.Headword);
        Assert.True(editor.HasPreview);
        Assert.Equal("", editor.Meaning);
        Assert.Null(store.Saved);

        editor.ApplyPreviewCommand.Execute(null);
        Assert.Equal("發票", editor.Meaning);
        Assert.Null(store.Saved);
        await editor.SaveCommand.ExecuteAsync();
        Assert.Null(store.Saved); // 詞性仍需由使用者確認。

        editor.PartOfSpeech = "noun";
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal("發票", store.Saved!.Meaning);
        Assert.Equal("ai", store.Saved.MeaningOrigin!.Kind);
        Assert.All(store.Saved.Examples, example => Assert.Equal("ai", example.Origin!.Kind));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task ChangedSenseCannotReceiveAnOlderAiPreview()
    {
        var store = new RecordingStore();
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { })
        { Headword = "invoice", AiConsent = true };
        await editor.GenerateCommand.ExecuteAsync();
        editor.Headword = "receipt";
        editor.ApplyPreviewCommand.Execute(null);

        Assert.Equal("", editor.Meaning);
        Assert.False(editor.HasPreview);
        Assert.Contains("已變更", editor.Error);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task ManualExampleChangesDoNotKeepAiAttribution()
    {
        var store = new RecordingStore();
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { })
        { Headword = "invoice", PartOfSpeech = "noun", AiConsent = true };
        await editor.GenerateCommand.ExecuteAsync();
        editor.ApplyPreviewCommand.Execute(null);
        editor.Examples[0].English = "I checked the invoice.";
        editor.Examples[0].Chinese = "我檢查了發票。";
        await editor.SaveCommand.ExecuteAsync();

        Assert.Equal("user", store.Saved!.Examples[0].Origin!.Kind);
        Assert.Equal("ai", store.Saved.MeaningOrigin!.Kind);
    }

    [Theory]
    [InlineData("meaning")]
    [InlineData("example")]
    [InlineData("new-example")]
    [InlineData("ai-preview")]
    public async Task EditsDuringSaveRemainDirtyAndDoNotTriggerNavigation(string editKind)
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore { SaveCompletion = release.Task };
        var original = new VocabularyItem
        {
            Headword = "invoice", PartOfSpeech = "noun", Meaning = "發票",
            Examples = [new("Please send the invoice.", "請寄送發票。")]
        };
        var callbacks = 0;
        var editor = new EditorViewModel(store, new FixedGenerator(), original, _ => callbacks++)
        { AiConsent = true };
        if (editKind == "ai-preview") await editor.GenerateCommand.ExecuteAsync();

        var saving = editor.SaveCommand.ExecuteAsync();
        Assert.True(editor.SaveCommand.IsRunning);
        Assert.Null(store.Saved);
        switch (editKind)
        {
            case "meaning":
                editor.Meaning = "請款單";
                break;
            case "example":
                editor.Examples[0].English = "Please check the invoice.";
                editor.Examples[0].Chinese = "請檢查發票。";
                break;
            case "new-example":
                editor.AddExampleCommand.Execute(null);
                editor.Examples[1].English = "I received the invoice.";
                editor.Examples[1].Chinese = "我收到了發票。";
                break;
            case "ai-preview":
                editor.ApplyPreviewCommand.Execute(null);
                Assert.False(editor.HasPreview);
                break;
        }
        release.SetResult(true);
        await saving;

        Assert.Equal("", editor.Error);
        Assert.Equal(original.Meaning, store.Saved!.Meaning);
        Assert.Equal(original.Examples.Select(example => (example.English, example.Chinese)),
            store.Saved.Examples.Select(example => (example.English, example.Chinese)));
        Assert.True(editor.IsDirty);
        Assert.Contains("新增的修改尚未保存", editor.Notice);
        Assert.Equal(0, callbacks);

        // 第二次保存包含目前畫面，才可清除 dirty 並通知外層導頁。
        await editor.SaveCommand.ExecuteAsync();
        Assert.False(editor.IsDirty);
        Assert.Equal(1, callbacks);
        Assert.Equal(editor.Meaning, store.Saved.Meaning);
        Assert.Equal(editor.Examples.Select(example => example.English),
            store.Saved.Examples.Select(example => example.English));
    }

    [Fact]
    public async Task AsyncCommandPreventsOverlappingSubmissions()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var errors = new List<string>();
        var command = new AsyncCommand(async token => { calls++; await release.Task.WaitAsync(token); }, errors.Add);

        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        release.SetResult(true);
        await first;
        Assert.True(command.CanExecute(null));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task UncertainReviewRetriesTheEntireOriginalSubmission()
    {
        var word = new VocabularyItem { Headword = "invoice", Meaning = "發票", PartOfSpeech = "noun", Enrollment = Enrollment.Selected };
        var store = new RecordingStore { NextReview = new(word, 7, false, DateTimeOffset.UtcNow.AddDays(-1)) };
        var submissions = new List<ReviewSubmission>();
        store.Submit = submission =>
        {
            submissions.Add(submission);
            if (submissions.Count == 1) throw new IOException("模擬回應遺失");
            store.NextReview = null;
            return Task.FromResult(new ReviewResult(submission.OperationId, submission.ReviewedAt.AddDays(3), "Review"));
        };
        var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
        await review.LoadAsync();
        review.FlipCommand.Execute(null);
        await review.GoodCommand.ExecuteAsync();
        Assert.Same(word, review.Current!.Word);
        Assert.True(review.IsAnswerVisible);

        await review.HardCommand.ExecuteAsync();
        Assert.Single(submissions);
        Assert.Contains("同一個評分", review.Error);

        await review.GoodCommand.ExecuteAsync();
        Assert.Equal(2, submissions.Count);
        Assert.Equal(submissions[0], submissions[1]);
        Assert.False(review.HasCard);
    }

    [Fact]
    public async Task ReviewCategoryPersistsForFollowingCardsAndUncertainRatingsCannotChangeScope()
    {
        var word = StudyTestData.Word();
        var store = new RecordingStore { NextReview = new(word, 0, true, null), Categories = ["旅行", "商業"] };
        var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
        await review.LoadAsync();
        Assert.Equal("全部單字庫", review.SelectedCategory);
        Assert.Null(store.RequestedCategory);

        review.SelectedCategory = "旅行";
        await review.ApplyCategoryCommand.ExecuteAsync();
        Assert.Equal("旅行", store.RequestedCategory);
        Assert.Contains("旅行", review.ScopeText);
        review.FlipCommand.Execute(null);
        store.Submit = _ => throw new IOException("回應遺失");
        await review.GoodCommand.ExecuteAsync();
        Assert.False(review.CanChangeCategory);
        Assert.False(review.ApplyCategoryCommand.CanExecute(null));

        store.Submit = submission => Task.FromResult(new ReviewResult(submission.OperationId, submission.ReviewedAt.AddDays(2), "Review"));
        await review.GoodCommand.ExecuteAsync();
        Assert.Equal("旅行", store.RequestedCategory);
        Assert.Equal("旅行", review.SelectedCategory);
        Assert.True(review.CanChangeCategory);
    }

    [Fact]
    public void EditorBackInvokesNavigationWithoutSavingDraft()
    {
        var store = new RecordingStore();
        var returned = false;
        var editor = new EditorViewModel(store, new FixedGenerator(), null, _ => { }, () => returned = true)
        { Headword = "draft" };
        editor.BackCommand.Execute(null);
        Assert.True(returned);
        Assert.True(editor.IsDirty);
        Assert.Null(store.Saved);
    }

    private sealed class SilentSpeech : IPronunciationService
    {
        public string Status => "test";
        public void Speak(string text) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FixedGenerator : IContentGenerator
    {
        public VocabularyItem? Request { get; private set; }
        public Task<string> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult("test");
        public Task<AiEnrichment> GenerateAsync(VocabularyItem word, CancellationToken cancellationToken = default)
        {
            Request = word;
            return Task.FromResult(new AiEnrichment("發票", ["send an invoice", "pay an invoice"],
                [new("Please send the invoice.", "請寄送發票。"), new("We paid the invoice yesterday.", "我們昨天付清了發票款項。")],
                new("ai", "固定測試內容")));
        }
    }

    private sealed class RecordingStore : IStudyStore
    {
        public VocabularyItem? Saved { get; private set; }
        public IReadOnlyList<VocabularyItem> SavedBatch { get; private set; } = [];
        public Task? SaveCompletion { get; set; }
        public ReviewItem? NextReview { get; set; }
        public string[] Categories { get; set; } = [];
        public IReadOnlyList<StudyActivity> Activity { get; set; } = [];
        public string? RequestedCategory { get; private set; }
        public Func<ReviewSubmission, Task<ReviewResult>>? Submit { get; set; }
        public async Task SaveVocabularyAsync(VocabularyItem item, CancellationToken cancellationToken = default)
        {
            if (SaveCompletion is { } pending) await pending.WaitAsync(cancellationToken);
            Saved = item;
        }
        public async Task SaveVocabularyBatchAsync(IReadOnlyList<VocabularyItem> items, CancellationToken cancellationToken = default)
        {
            if (SaveCompletion is { } pending) await pending.WaitAsync(cancellationToken);
            SavedBatch = items;
            Saved = items[0];
        }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportSeedPackAsync(SeedPack pack, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<VocabularyItem>> GetVocabularyAsync(string? search = null, string? category = null, CancellationToken cancellationToken = default, bool includeArchived = false) => Task.FromResult<IReadOnlyList<VocabularyItem>>(Saved is null || (Saved.IsArchived && !includeArchived) ? [] : [Saved]);
        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Categories);
        public Task<IReadOnlyList<StudyActivity>> GetStudyActivityAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default) => Task.FromResult(Activity);
        public Task AddCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetEnrollmentAsync(Guid senseId, Enrollment enrollment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPausedAsync(Guid senseId, bool paused, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetStarredAsync(Guid senseId, bool starred, CancellationToken cancellationToken = default)
        {
            if (Saved?.Id == senseId) Saved = Saved with { IsStarred = starred };
            if (NextReview?.Word.Id == senseId) NextReview = NextReview with { Word = NextReview.Word with { IsStarred = starred } };
            return Task.CompletedTask;
        }
        public Task ArchiveAsync(Guid senseId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DashboardSummary> GetDashboardAsync(DateTimeOffset now, CancellationToken cancellationToken = default, string? category = null, IReadOnlyList<string>? categories = null) => Task.FromResult(new DashboardSummary(0, 0, 0, 0, 0, null));
        public Task<ReviewItem?> GetNextReviewAsync(DateTimeOffset now, System.Numerics.BigInteger dailyNewLimit, CancellationToken cancellationToken = default, string? category = null, IReadOnlyList<string>? categories = null)
        {
            RequestedCategory = category;
            return Task.FromResult(NextReview);
        }
        public Task<ReviewResult> SubmitReviewAsync(ReviewSubmission submission, CancellationToken cancellationToken = default) => Submit?.Invoke(submission) ?? throw new NotSupportedException();
        public Task UndoReviewAsync(Guid operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EndReviewSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
