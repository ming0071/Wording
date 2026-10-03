using System.Text.Json;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class VocabularyStarImportTests
{
    [Fact]
    public async Task JsonStarsRoundTripAndOmittedStarsPreserveExistingSelection()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var original = await data.AddWordAsync();
        await data.Store.SetStarredAsync(original.Id, true);
        var service = new VocabularyFileService(data.Store);
        var path = Path.Combine(data.DirectoryPath, "stars.json");
        await service.ExportAsync(path);
        var exported = JsonSerializer.Deserialize<VocabularyDocument>(await File.ReadAllTextAsync(path), VocabularyFileService.JsonOptions)!;
        Assert.True(Assert.Single(exported.Items).IsStarred);
        var oldEntry = exported.Items[0] with { IsStarred = null, Notes = "沒有指定星號" };
        await WriteAsync(path, [oldEntry, new() { Headword = "employ", PartOfSpeech = "v", Meaning = "雇用", IsStarred = true }]);
        Assert.DoesNotContain("isStarred", JsonSerializer.Serialize(oldEntry, VocabularyFileService.JsonOptions));
        await service.ImportAsync(path);
        var all = await data.Store.GetVocabularyAsync();
        Assert.All(all, x => Assert.True(x.IsStarred));
        var createdAt = all.Single(x => x.Id == original.Id).CreatedAt;
        await WriteAsync(path, [oldEntry with { IsStarred = false }]);
        await service.ImportAsync(path);
        var updated = (await data.Store.GetVocabularyAsync()).Single(x => x.Id == original.Id);
        Assert.False(updated.IsStarred);
        Assert.Equal(createdAt, updated.CreatedAt);
        await WriteAsync(path, [oldEntry with { IsStarred = true }]);
        await service.ImportAsync(path);
        Assert.True((await data.Store.GetVocabularyAsync()).Single(x => x.Id == original.Id).IsStarred);
        await service.ExportAsync(path);
        using var second = new StudyTestData();
        await second.Store.InitializeAsync();
        await new VocabularyFileService(second.Store).ImportAsync(path);
        Assert.All(await second.Store.GetVocabularyAsync(), x => Assert.True(x.IsStarred));
    }

    [Fact]
    public async Task FailedImportRollsBackStarsAndContentTogether()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var original = await data.AddWordAsync();
        var path = Path.Combine(data.DirectoryPath, "stars.json");
        await WriteAsync(path, [VocabularyEntry.From(original) with { IsStarred = true, Notes = "不應寫入" }]);
        data.FailAt = "Vocabulary.AfterItem";
        await Assert.ThrowsAsync<IOException>(() => new VocabularyFileService(data.Store).ImportAsync(path));
        var unchanged = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.False(unchanged.IsStarred);
        Assert.Equal(original.Notes, unchanged.Notes);
    }

    private static Task WriteAsync(string path, VocabularyEntry[] items) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(new VocabularyDocument(1, items), VocabularyFileService.JsonOptions));
}
