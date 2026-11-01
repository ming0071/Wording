using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class BundledVocabularyTests
{
    [Fact]
    public async Task BundledPacksRequireExplicitImportAndPreservePersonalWordAndProgress()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("my private vocabulary") with { IsStarred = true, Notes = "自己的筆記" };
        await data.Store.SaveVocabularyAsync(word);
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Easy, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion));
        var before = data.Scalar("SELECT after_json FROM review_log;");
        var vm = new SettingsViewModel(new BackupService(data.Store), new PracticeFixtures.Generator(), new PracticeFixtures.Speech(),
            new(), new(), data.DirectoryPath, () => { }, data.Store);
        Assert.Equal(3, vm.BundledVocabulary.Length);
        Assert.Single(await data.Store.GetVocabularyAsync());
        vm.SelectedBundledVocabulary = vm.BundledVocabulary.Single(x => x.Path.EndsWith("toeic-starter.json"));
        await vm.ImportBundledCommand.ExecuteAsync();
        Assert.Equal("", vm.Error);
        Assert.True((await data.Store.GetVocabularyAsync()).Count > 250);
        var preserved = (await data.Store.GetVocabularyAsync()).Single(x => x.Id == word.Id);
        Assert.True(preserved.IsStarred);
        Assert.Equal("自己的筆記", preserved.Notes);
        Assert.Equal(before, data.Scalar("SELECT after_json FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
    }
}
