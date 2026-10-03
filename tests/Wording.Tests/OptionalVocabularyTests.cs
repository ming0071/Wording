using System.Text.Json;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class OptionalVocabularyTests
{
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
    public async Task OriginalStarterPackCanStillBeImportedManually()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var path = Path.Combine(AppContext.BaseDirectory, "content", "toeic-starter.json");
        Assert.Equal(300, await new VocabularyFileService(data.Store).ImportAsync(path));
        Assert.Equal(300, (await data.Store.GetVocabularyAsync()).Count);
    }
}
