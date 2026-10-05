using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReviewMeaningsUseEqualColumnsAndTheSameTypography(int count)
    {
        OffscreenWpf.Invoke(() =>
        {
            var word = ReviewColumnWord(count);
            var store = new RecordingStore { NextReview = new(word, 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false });
            review.LoadAsync().GetAwaiter().GetResult();
            var view = new ReviewView { DataContext = review, FontSize = 14,
                FontFamily = new("Segoe UI, Microsoft JhengHei UI"), Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            Layout(view, 959, 710);
            Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(), x => IsShown(x) && AutomationProperties.GetAutomationId(x) == "ReviewMeaning");
            review.FlipCommand.Execute(null);
            foreach (var width in new[] { 715, 959, 1850 })
            {
                Layout(view, width, width == 715 ? 560 : 900);
                var columns = Descendants(view).OfType<Border>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewDefinition").ToArray();
                Assert.Equal(count, columns.Length);
                var bounds = columns.Select(x => x.TransformToAncestor(view).TransformBounds(new Rect(0, 0, x.ActualWidth, x.ActualHeight))).ToArray();
                for (var index = 0; index < count; index++)
                {
                    Assert.InRange(bounds[index].Top, bounds[0].Top - 0.1, bounds[0].Top + 0.1);
                    Assert.InRange(bounds[index].Width, bounds[0].Width - 0.1, bounds[0].Width + 0.1);
                    if (index > 0) Assert.True(bounds[index].Left >= bounds[index - 1].Right);
                    var texts = Descendants(columns[index]).OfType<TextBlock>().Where(IsShown).ToArray();
                    var definition = word.Definitions[index];
                    foreach (var text in new[] { definition.Meaning, definition.EnglishDefinition, definition.Notes,
                        string.Join(" · ", definition.Collocations), string.Join("、", definition.Synonyms) })
                        Assert.Contains(texts, x => x.Text == text);
                    Assert.All(texts, text => AssertFits(view, text));
                    var meaning = Assert.Single(texts, x => x.Text == definition.Meaning);
                    Assert.Equal(22, meaning.FontSize);
                    Assert.Equal(FontWeights.SemiBold, meaning.FontWeight);
                    foreach (var example in definition.Examples)
                    {
                        Assert.Equal(23, Assert.Single(texts, x => x.Text == example.English).FontSize);
                        Assert.Equal(18, Assert.Single(texts, x => x.Text == example.Chinese).FontSize);
                    }
                }
                Assert.Single(Descendants(view).OfType<Button>(), x => x.Tag is bool);
                AssertFits(view, "RateGood");
                if (width != 715) Render(view, $"review-v042-{count}-meanings-{width}.png");
            }
        });
    }

    [Fact]
    public async Task ReviewColumnsRefreshAcrossCardsAndSpeechIncludesEveryMeaning()
    {
        var speech = new RecordingSpeech();
        var store = new RecordingStore();
        var review = new ReviewViewModel(store, speech, new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false });
        foreach (var count in new[] { 3, 1, 2 })
        {
            var word = ReviewColumnWord(count);
            store.NextReview = new(word, 0, true, null);
            await review.LoadAsync();
            Assert.Equal(count, review.DefinitionColumns);
            Assert.Equal(count, review.Definitions.Count);
            review.FlipCommand.Execute(null);
            review.SpeakExampleCommand.Execute(null);
            Assert.Equal(string.Join(" ", word.Definitions.SelectMany(x => x.Examples).Select(x => x.English)), speech.Spoken.Last());
        }
        store.NextReview = null;
        await review.LoadAsync();
        Assert.Empty(review.Definitions);
        Assert.Equal(1, review.DefinitionColumns);
        Assert.False(review.SpeakExampleCommand.CanExecute(null));
        store.NextReview = new(ReviewColumnWord(2) with { Examples = [] }, 0, true, null);
        await review.LoadAsync();
        review.FlipCommand.Execute(null);
        Assert.True(review.SpeakExampleCommand.CanExecute(null));
    }

    private static VocabularyItem ReviewColumnWord(int count)
    {
        var definitions = new VocabularyDefinition[]
        {
            new() { PartOfSpeech = "名詞", Cue = "A company location", Meaning = "分公司；分行",
                EnglishDefinition = "A local office of a company.", Collocations = ["local branch", "open a branch"], Synonyms = ["office"],
                Notes = "公司或銀行在不同地點設立的據點。", Examples = [new("The company will open a new branch in Kaohsiung.", "這家公司將在高雄開設新的分公司。")] },
            new() { PartOfSpeech = "名詞", Cue = "Part of a tree", Meaning = "樹枝",
                EnglishDefinition = "A part growing out of a tree trunk.", Collocations = ["a tree branch"], Synonyms = ["limb"], Notes = "用於描述樹木的枝條。",
                Examples = [new("A bird is resting on the branch.", "一隻鳥停在樹枝上。"), new("The branch broke in the storm.", "樹枝在暴風雨中折斷了。")] },
            new() { PartOfSpeech = "動詞", Cue = "Divide into separate paths", Meaning = "分岔；分支",
                EnglishDefinition = "To divide into separate paths.", Collocations = ["branch off"], Synonyms = ["divide"], Notes = "可搭配 off 表示從主要路線分岔。",
                Examples = [new("The road branches off near the station.", "道路在車站附近分岔。")] }
        };
        return definitions[0].AsWord("branch") with { AdditionalSenses = definitions.Skip(1).Take(count - 1).ToArray() };
    }
}
