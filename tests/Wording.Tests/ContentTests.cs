using System.Text.Json;
using System.Text.Json.Serialization;
using Wording.Core;
using Xunit;

namespace Wording.Tests;

public sealed class ContentTests
{
    private static SeedPack LoadPack()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "content", "toeic-starter.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<SeedPack>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("The starter pack is empty.");
    }

    [Fact]
    public void ShippedPackDeserializesIntoCoreContractAndRequiresLearnerSelection()
    {
        var pack = LoadPack();
        Assert.Equal("toeic-starter", pack.PackId);
        Assert.Equal(1, pack.Version);
        Assert.Equal(297, pack.Items.Length);
        Assert.Equal(300, pack.Items.SelectMany(item => item.Definitions).Select(x => x.Id).Distinct().Count());
        Assert.All(pack.Items, item =>
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.Equal(Enrollment.Candidate, item.Enrollment);
            Assert.False(item.IsArchived);
            Assert.False(item.IsPaused);
            Assert.False(item.IsUserEdited);
            Assert.False(string.IsNullOrWhiteSpace(item.Headword));
            Assert.False(string.IsNullOrWhiteSpace(item.PartOfSpeech));
            Assert.False(string.IsNullOrWhiteSpace(item.Meaning));
            Assert.False(string.IsNullOrWhiteSpace(item.Cue));
            Assert.NotEmpty(item.Categories);
            Assert.NotEmpty(item.Collocations);
            Assert.NotEmpty(item.Examples);
            Assert.All(item.Examples, example =>
            {
                Assert.False(string.IsNullOrWhiteSpace(example.English));
                Assert.False(string.IsNullOrWhiteSpace(example.Chinese));
            });
            Assert.Equal("ai", item.Origin.Kind);
            Assert.Equal("AI 編寫的原創學習教材；非官方 TOEIC 詞表", item.Origin.Note);
        });
    }

    [Theory]
    [InlineData("address", "處理；設法解決", "地址")]
    [InlineData("charge", "收費；費用", "為電池充電")]
    [InlineData("issue", "核發；發給", "問題；需要處理的事項")]
    public void DifferentMeaningsShareOneStoredWordWithSeparateDefinitionContent(
        string headword, string firstMeaning, string secondMeaning)
    {
        var word = Assert.Single(LoadPack().Items, item => item.Headword == headword);
        var senses = word.Definitions.ToArray();
        Assert.Equal(2, senses.Length);
        Assert.Contains(senses, sense => sense.Meaning == firstMeaning);
        Assert.Contains(senses, sense => sense.Meaning == secondMeaning);
        Assert.NotEqual(senses[0].Id, senses[1].Id);
        Assert.NotEqual(senses[0].Cue, senses[1].Cue);
    }
}
