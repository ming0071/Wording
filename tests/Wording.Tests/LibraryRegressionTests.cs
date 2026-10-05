using System.Windows;
using System.Windows.Controls;
using Wording.Core;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData(" colleague ", "colleague")]
    [InlineData("COLLEAGUE", "colleague")]
    [InlineData("同事", "colleague")]
    [InlineData("填寫表格", "fill in")]
    [InlineData("fill", "fill in")]
    [InlineData("hidden-only", null)]
    [InlineData("  ", null)]
    public async Task LibrarySearchMatchesOnlyHeadwordsAndCurrentMeanings(string search, string? expectedHeadword)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var phrase = StudyTestData.Word("fill in", "暫代他人的工作；代班") with
        {
            Collocations = ["fill in for a colleague", "hidden-only"],
            Cue = "While a colleague is away",
            EnglishDefinition = "Cover for a colleague.",
            Notes = "colleague",
            AdditionalSenses = [new()
            {
                Meaning = "填寫表格", Collocations = ["colleague", "hidden-only"],
                Examples = [new("Help a colleague with the form.", "幫忙填表。")]
            }]
        };
        await data.Store.SaveVocabularyBatchAsync([phrase, StudyTestData.Word("colleague", "同事；同僚")]);
        var library = new LibraryViewModel(data.Store, _ => { }) { SearchText = search };
        await library.LoadAsync();
        var stored = await data.Store.GetVocabularyAsync(search);
        if (expectedHeadword is not null)
        {
            Assert.Equal(expectedHeadword, Assert.Single(library.Items).Headword);
            Assert.Equal(expectedHeadword, Assert.Single(stored).Headword);
        }
        else if (string.IsNullOrWhiteSpace(search))
        {
            Assert.Equal(2, library.Items.Count);
            Assert.Equal(2, stored.Count);
        }
        else
        {
            Assert.Empty(library.Items);
            Assert.Empty(stored);
        }
    }

    [Fact]
    public async Task LibraryCentersMeaningsAfterRemovingAnAdditionalSense()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("colleague", "同事；同僚") with
        {
            AdditionalSenses = [new() { PartOfSpeech = "名詞", Meaning = "同事；同僚" }]
        };
        await data.Store.SaveVocabularyAsync(word);
        await OffscreenWpf.InvokeAsync(async () =>
        {
            var library = new LibraryViewModel(data.Store, _ => { });
            await library.LoadAsync();
            var view = new LibraryView { DataContext = library, FontSize = 14 };
            AssertMeaningsCentered(view, 2);
            var editor = new EditorViewModel(data.Store, new FixedGenerator(), library.Items.Single(), _ => { });
            await editor.LoadAsync();
            editor.RemoveSenseCommand.Execute(editor.AdditionalSenses.Single());
            await editor.SaveCommand.ExecuteAsync();
            Assert.Empty(editor.Error);
            await library.LoadAsync();
            Assert.Single(library.Items.Single().Definitions);
            foreach (var width in new[] { 715, 959, 1850 })
            {
                AssertMeaningsCentered(view, 1, width);
                if (width == 959) Render(view, "library-single-meaning-centered.png");
            }
            library.SearchText = "同事";
            await library.LoadAsync();
            AssertMeaningsCentered(view, 1);
        });
    }

    private static void AssertMeaningsCentered(LibraryView view, int count, double width = 959)
    {
        Layout(view, width, 710);
        var row = Assert.Single(Descendants(view).OfType<DataGridRow>());
        var meanings = Assert.Single(Descendants(row).OfType<ItemsControl>(), x => x.GetType() == typeof(ItemsControl));
        Assert.Equal(count, meanings.Items.Count);
        var center = meanings.TranslatePoint(new Point(0, meanings.ActualHeight / 2), row).Y;
        Assert.InRange(center, row.ActualHeight / 2 - 1, row.ActualHeight / 2 + 1);
        Assert.All(Descendants(meanings).OfType<TextBlock>(), block => AssertFits(view, block));
    }
}
