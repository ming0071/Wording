using System.Text.Json;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class OptionalVocabularyTests
{
    [Theory]
    [InlineData("toeic-vocabulary.json")]
    [InlineData("toeic-starter.json")]
    [InlineData("toeic-phrasal-verbs.json")]
    public async Task ExplicitContentIdsPreserveReviewedWordsWhenReimportedAndEdited(string fileName)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var path = Path.Combine(AppContext.BaseDirectory, "content", fileName);
        var document = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path),
            VocabularyFileService.JsonOptions)!;
        Assert.All(document.Items, entry => Assert.NotEqual(Guid.Empty, entry.Id));
        Assert.Equal(document.Items.Length, document.Items.Select(entry => entry.Id).Distinct().Count());
        var service = new VocabularyFileService(data.Store);
        await service.ImportAsync(path);
        var target = document.Items[0];
        await data.Store.SubmitReviewAsync(new(target.Id, Wording.Core.ReviewRating.Good,
            data.Clock.Now, Guid.NewGuid(), 0));
        var schedule = data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{target.Id:D}';");
        await service.ImportAsync(path);
        var edited = document with { Items = [target with { Meaning = "更新後的中文解釋" }, .. document.Items.Skip(1)] };
        var editedPath = Path.Combine(data.DirectoryPath, "edited.json");
        await File.WriteAllTextAsync(editedPath, JsonSerializer.Serialize(edited, VocabularyFileService.JsonOptions));
        await service.ImportAsync(editedPath);
        var words = await data.Store.GetVocabularyAsync();
        Assert.Equal(document.Items.OrderBy(entry => entry.Id).Select(entry => entry.Id), words.OrderBy(word => word.Id).Select(word => word.Id));
        Assert.Equal("更新後的中文解釋", words.Single(word => word.Id == target.Id).Meaning);
        Assert.Equal(schedule, data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{target.Id:D}';"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
    }

    [Fact]
    public async Task SharedVocabularyImportsIntoEmptyLibraryWithoutAuthorsLearningHistory()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        Assert.Empty(await data.Store.GetVocabularyAsync());
        var path = Path.Combine(AppContext.BaseDirectory, "content", "toeic-vocabulary.json");
        var document = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path),
            VocabularyFileService.JsonOptions)!;
        Assert.NotEmpty(document.Items);
        var service = new VocabularyFileService(data.Store);
        Assert.Equal(document.Items.Length, await service.ImportAsync(path));
        var imported = await data.Store.GetVocabularyAsync();
        Assert.Equal(document.Items.Length, imported.Count);
        Assert.Equal(document.Items.Count(x => x.IsStarred == true), imported.Count(x => x.IsStarred));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal(0L, data.Scalar("SELECT COUNT(*) FROM cards WHERE last_review_ms IS NOT NULL OR version <> 0;"));
        var exportedPath = Path.Combine(data.DirectoryPath, "roundtrip.json");
        await service.ExportAsync(exportedPath);
        var exported = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(exportedPath),
            VocabularyFileService.JsonOptions)!;
        Assert.Equal(document.Items.OrderBy(x => x.Id).Select(x => JsonSerializer.Serialize(x, VocabularyFileService.JsonOptions)),
            exported.Items.OrderBy(x => x.Id).Select(x => JsonSerializer.Serialize(x, VocabularyFileService.JsonOptions)));
        await service.ImportAsync(path);
        Assert.Equal(document.Items.Length, (await data.Store.GetVocabularyAsync()).Count);
    }

    [Fact]
    public async Task VerbPhraseSupplementAddsTopicAndSensesWithoutResettingPersonalLearning()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var service = new VocabularyFileService(data.Store);
        var path = Path.Combine(AppContext.BaseDirectory, "content", "toeic-phrasal-verbs.json");
        var supplement = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path),
            VocabularyFileService.JsonOptions)!;
        var reviewed = supplement.Items.Single(x => x.Headword == "apply for");
        var unstarred = supplement.Items.Single(x => x.Headword == "check out" && x.Meaning == "辦理退房");
        var originalPath = Path.Combine(data.DirectoryPath, "original.json");
        var original = new VocabularyDocument(1,
        [
            reviewed with { IsStarred = true, Categories = reviewed.Categories.Where(x => x != "動詞片語").ToArray() },
            unstarred with { IsStarred = false, Categories = unstarred.Categories.Where(x => x != "動詞片語").ToArray() }
        ]);
        await File.WriteAllTextAsync(originalPath, JsonSerializer.Serialize(original, VocabularyFileService.JsonOptions));
        await service.ImportAsync(originalPath);
        var privateWord = StudyTestData.Word("personal reminder") with { Notes = "自己的筆記", IsStarred = true };
        await data.Store.SaveVocabularyAsync(privateWord);
        await data.Store.SubmitReviewAsync(new(reviewed.Id, Wording.Core.ReviewRating.Good,
            data.Clock.Now, Guid.NewGuid(), 0));
        var due = data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{reviewed.Id:D}';");
        var version = data.Scalar($"SELECT version FROM cards WHERE sense_id='{reviewed.Id:D}';");

        Assert.Equal(99, await service.ImportAsync(path));
        Assert.Equal(99, await service.ImportAsync(path));
        var words = await data.Store.GetVocabularyAsync();
        Assert.Equal(100, words.Count);
        Assert.True(words.Single(x => x.Id == reviewed.Id).IsStarred);
        Assert.False(words.Single(x => x.Id == unstarred.Id).IsStarred);
        Assert.All(original.Items, entry =>
        {
            var word = words.Single(x => x.Id == entry.Id);
            Assert.Contains("動詞片語", word.Categories);
            Assert.All(entry.Categories, category => Assert.Contains(category, word.Categories));
        });
        var preserved = words.Single(x => x.Id == privateWord.Id);
        Assert.Equal("自己的筆記", preserved.Notes);
        Assert.True(preserved.IsStarred);
        Assert.DoesNotContain("動詞片語", preserved.Categories);
        Assert.Equal(due, data.Scalar($"SELECT due_ms FROM cards WHERE sense_id='{reviewed.Id:D}';"));
        Assert.Equal(version, data.Scalar($"SELECT version FROM cards WHERE sense_id='{reviewed.Id:D}';"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        Assert.Equal(2, words.Single(x => x.Headword == "fill in").Definitions.Count);
        Assert.Equal(2, words.Single(x => x.Headword == "check out").Definitions.Count);

        var fullPath = Path.Combine(AppContext.BaseDirectory, "content", "toeic-vocabulary.json");
        var full = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(fullPath),
            VocabularyFileService.JsonOptions)!;
        Assert.Equal(1113, full.Items.Length);
        Assert.Equal(1385, full.Items.Sum(x => 1 + x.AdditionalSenses.Length));
        Assert.Equal(9, full.Items.SelectMany(x => x.Categories).Distinct().Count());
        Assert.All(supplement.Items, entry =>
        {
            Assert.Null(entry.IsStarred);
            Assert.Contains("動詞片語", entry.Categories);
            Assert.True(entry.Categories.Length >= 2);
            var shared = full.Items.Single(x => x.Id == entry.Id) with { IsStarred = null };
            Assert.Equal(JsonSerializer.Serialize(shared, VocabularyFileService.JsonOptions),
                JsonSerializer.Serialize(entry, VocabularyFileService.JsonOptions));
        });
    }

    [Fact]
    public async Task DocumentedVocabularyExampleImportsWithStableIdentityAndPreservesReviewOnReimport()
    {
        using var data = new StudyTestData(); await data.Store.InitializeAsync();
        var service = new VocabularyFileService(data.Store);
        var path = Path.Combine(AppContext.BaseDirectory, "content", "vocabulary-example.json");
        Assert.Equal(1, await service.ImportAsync(path));
        var first = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal("bank", first.Headword); Assert.Equal("河岸", first.Meaning); Assert.Equal("riverside", Assert.Single(first.Synonyms));
        await data.Store.SubmitReviewAsync(new(first.Id, Wording.Core.ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        Assert.Equal(1, await service.ImportAsync(path));
        Assert.Equal(first.Id, Assert.Single(await data.Store.GetVocabularyAsync()).Id);
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM cards WHERE last_review_ms IS NOT NULL;"));
    }

    [Fact]
    public async Task OriginalStarterPackCanStillBeImportedManually()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var path = Path.Combine(AppContext.BaseDirectory, "content", "toeic-starter.json");
        Assert.Equal(297, await new VocabularyFileService(data.Store).ImportAsync(path));
        Assert.Equal(300, (await data.Store.GetVocabularyAsync()).Sum(x => x.Definitions.Count));
    }
}
