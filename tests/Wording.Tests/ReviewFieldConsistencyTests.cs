using System.Text.Json;
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
    [InlineData("n", "名詞")]
    [InlineData(" N. ", "名詞")]
    [InlineData("noun", "名詞")]
    [InlineData("名詞", "名詞")]
    [InlineData("n[C]", "可數名詞")]
    [InlineData("n[U]", "不可數名詞")]
    [InlineData("v", "動詞")]
    [InlineData("verb", "動詞")]
    [InlineData("vi", "不及物動詞")]
    [InlineData("vt", "及物動詞")]
    [InlineData("adj", "形容詞")]
    [InlineData("adv", "副詞")]
    [InlineData("prep", "介系詞")]
    [InlineData("conj", "連接詞")]
    [InlineData("aux", "助動詞")]
    [InlineData("int", "感嘆詞")]
    [InlineData("pron", "代名詞")]
    [InlineData("phrase", "片語")]
    [InlineData("phrasal verb", "動詞片語")]
    [InlineData("n, adj, adv, pron", "名詞、形容詞、副詞、代名詞")]
    [InlineData("adj / adv", "形容詞、副詞")]
    [InlineData("自訂詞性", "自訂詞性")]
    public void ReviewPartOfSpeechUsesChineseWithoutChangingCustomLabels(string stored, string expected) =>
        Assert.Equal(expected, PartOfSpeechDisplay.Chinese(stored));

    [Fact]
    public void AssignmentWithMissingFieldsKeepsReviewSectionsAligned()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "toeic-vocabulary.json")));
        var word = document.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("headword").GetString() == "assignment")
            .Deserialize<VocabularyItem>(VocabularyFileService.JsonOptions)!;
        Assert.Equal("", word.EnglishDefinition);
        Assert.Equal("n", word.AdditionalSenses[0].PartOfSpeech);
        OffscreenWpf.Invoke(() =>
        {
            var store = new RecordingStore { NextReview = new(word, 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false, AutoSpeakExamples = false });
            review.LoadAsync().GetAwaiter().GetResult();
            var view = new ReviewView { DataContext = review, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            Layout(view, 959, 710);
            var questionPart = Assert.Single(Descendants(view).OfType<TextBlock>(), x => AutomationProperties.GetAutomationId(x) == "ReviewQuestionPartOfSpeech");
            Assert.Equal("名詞", questionPart.Text);
            review.FlipCommand.Execute(null);
            foreach (var width in new[] { 715, 959, 1850 })
            {
                Layout(view, width, width == 715 ? 560 : 900);
                foreach (var id in new[] { "ReviewPartOfSpeech", "ReviewMeaning", "ReviewEnglishDefinitionSection", "ReviewExamples" })
                {
                    var sections = Descendants(view).OfType<FrameworkElement>().Where(x => AutomationProperties.GetAutomationId(x) == id).ToArray();
                    Assert.Equal(3, sections.Length);
                    for (var index = 0; index < sections.Length; index++)
                    {
                        var section = sections[index];
                        var top = sections[index / review.DefinitionColumns * review.DefinitionColumns].TranslatePoint(new Point(), view).Y;
                        Assert.InRange(section.TranslatePoint(new Point(), view).Y, top - 0.5, top + 0.5);
                        AssertFitsReviewContent(view, section);
                    }
                }
                var parts = Descendants(view).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewPartOfSpeech").ToArray();
                Assert.All(parts, part => Assert.Equal("名詞", part.Text));
                var definitions = Descendants(view).OfType<TextBlock>().Where(x => AutomationProperties.GetAutomationId(x) == "ReviewEnglishDefinition").ToArray();
                Assert.Equal("尚未提供英文解釋", definitions[0].Text);
                Assert.Equal(word.AdditionalSenses[0].EnglishDefinition, definitions[1].Text);
                Assert.Equal(word.AdditionalSenses[1].EnglishDefinition, definitions[2].Text);
                Assert.All(definitions, definition => { Assert.Equal(15, definition.FontSize); Assert.Equal(TextAlignment.Center, definition.TextAlignment); });
                Assert.Equal(word.Cue, Assert.Single(Descendants(view).OfType<TextBlock>(), x =>
                    IsShown(x) && AutomationProperties.GetAutomationId(x) == "ReviewCue").Text);
                Assert.All(Descendants(view).OfType<TextBlock>().Where(IsShown), block => AssertFitsReviewContent(view, block));
                Render(view, $"review-v044-assignment-{width}.png");
            }
            store.NextReview = new(word with { AdditionalSenses = [] }, 0, true, null);
            review.LoadAsync().GetAwaiter().GetResult();
            review.FlipCommand.Execute(null);
            Layout(view, 959, 710);
            Assert.Single(Descendants(view).OfType<Border>(), x => AutomationProperties.GetAutomationId(x) == "ReviewDefinition");
            AssertFits(view, "ReviewMeaning");
            Assert.Equal("n", word.AdditionalSenses[0].PartOfSpeech);
            Assert.Empty(word.EnglishDefinition);
        });
    }
}
