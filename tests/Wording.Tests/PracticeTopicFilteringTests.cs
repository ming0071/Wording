using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class PracticeTopicFilteringTests
{
    [Theory]
    [InlineData(PracticeMode.Reading, false)]
    [InlineData(PracticeMode.Reading, true)]
    [InlineData(PracticeMode.Listening, false)]
    [InlineData(PracticeMode.Listening, true)]
    public async Task ClassificationIsHiddenInPracticeWhileVocabularyAndHistoryRemain(PracticeMode mode, bool onlyClassification)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var phrase = StudyTestData.Word("check in") with { Categories = ["動詞片語", "旅行"] };
        await data.Store.SaveVocabularyAsync(phrase);
        await data.Store.SaveVocabularyAsync(StudyTestData.Word("office") with { Categories = ["旅行"] });
        await data.Store.CompletePracticeAsync(new(Guid.NewGuid(), data.Clock.Now, mode, ["動詞片語"], [phrase.Id]));
        await data.Store.CompletePracticeAsync(new(Guid.NewGuid(), data.Clock.Now, mode, ["動詞片語", "旅行"], [phrase.Id]));
        var settings = new AppSettings
        {
            AutoSpeakWord = false,
            Practice = new() { Mode = mode, Topics = onlyClassification ? ["動詞片語"] : ["動詞片語", "旅行"] }
        };
        var generator = new PracticeFixtures.Generator();
        var saves = 0;
        var vm = new PracticeViewModel(data.Store, data.Store, generator, new PracticeFixtures.Speech(),
            settings, () => saves++, _ => { }, clock: data.Clock);
        await vm.LoadAsync();

        Assert.DoesNotContain(vm.Topics.Choices, x => x.Name == "動詞片語");
        Assert.Equal(onlyClassification ? Array.Empty<string>() : ["旅行"], vm.Topics.SelectedNames);
        Assert.Equal(onlyClassification, vm.Topics.Choices[0].IsSelected);
        Assert.DoesNotContain(vm.TopicActivities, x => x.Name == "動詞片語");
        Assert.Equal(1, vm.TopicActivities.Single(x => x.Name == "旅行").Count);
        Assert.Equal(2, vm.CompletedCount);
        Assert.Equal(2, (await data.Store.GetPracticeHistoryAsync(data.Clock.Now)).Count);

        await vm.GenerateCommand.ExecuteAsync();
        Assert.Equal("", vm.Error);
        Assert.True(vm.HasMaterial);
        Assert.DoesNotContain("動詞片語", vm.ExerciseLabel);
        Assert.Contains(generator.Last!.Targets, x => x.SenseId == phrase.Id);
        Assert.DoesNotContain("動詞片語", settings.Practice.Topics);
        Assert.Equal(1, saves);
        Assert.Contains("動詞片語", await data.Store.GetCategoriesAsync());
        Assert.Contains((await data.Store.GetVocabularyAsync(category: "動詞片語")), x => x.Id == phrase.Id);
        var review = new ReviewViewModel(data.Store, new PracticeFixtures.Speech(), settings);
        await review.LoadAsync();
        Assert.Contains("動詞片語", review.Categories);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TopicSelectionExcludesClassificationButStillUsesPhrases(int savedSelection)
    {
        var now = DateTimeOffset.UtcNow;
        var phrase = StudyTestData.Word("check in") with { Categories = ["動詞片語", "旅行"] };
        var candidates = new[] { Candidate(phrase, now) };
        var options = new PracticeOptions
        {
            Topics = savedSelection switch { 0 => [], 1 => ["動詞片語"], _ => ["動詞片語", "旅行"] }
        };
        for (var seed = 0; seed < 20; seed++)
        {
            var request = new PracticeSelector(new Random(seed)).Select(options, candidates, [], now);
            Assert.Equal(["旅行"], request.Options.Topics);
            Assert.Equal(phrase.Id, Assert.Single(request.Targets).Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VocabularyWithOnlyClassificationCanStillGenerateFreeScenarios(bool oldSelection)
    {
        var now = DateTimeOffset.UtcNow;
        var phrase = StudyTestData.Word("check in") with { Categories = ["動詞片語"] };
        var request = new PracticeSelector(new Random(1)).Select(
            new() { Topics = oldSelection ? ["動詞片語"] : [] }, [Candidate(phrase, now)], [], now);
        Assert.Equal(["自由情境"], request.Options.Topics);
        Assert.Equal(phrase.Id, Assert.Single(request.Targets).Id);
    }

    private static PracticeCandidate Candidate(VocabularyItem word, DateTimeOffset now) =>
        new(word, new(Guid.NewGuid(), LearningState.Learning, 0, null, null, now, null));
}
