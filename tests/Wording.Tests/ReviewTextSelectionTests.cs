using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData("colleague")]
    [InlineData("follow up")]
    [InlineData("mother-in-law")]
    public void DoubleClickSelectsTheWholeHeadwordAndCopyKeepsReviewShortcuts(string word)
    {
        OffscreenWpf.Invoke(() =>
        {
            var store = new RecordingStore { NextReview = new(StudyTestData.Word(word), 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings { AutoSpeakWord = false });
            review.LoadAsync().GetAwaiter().GetResult();
            var view = new ReviewView { DataContext = review };
            Layout(view, 959, 710);
            var headword = Assert.Single(Descendants(view).OfType<TextBox>(), x =>
                AutomationProperties.GetAutomationId(x) == "ReviewHeadword");
            Assert.True(headword.IsReadOnly);
            Assert.Equal(word, headword.Text);

            string? copied = null;
            DataObject.AddCopyingHandler(headword, (_, e) =>
            {
                copied = e.DataObject.GetData(DataFormats.UnicodeText) as string;
                // Inspect the actual native copy payload without changing the user's clipboard.
                e.CancelCommand();
            });
            void SelectAndCopy()
            {
                headword.Select(0, 0);
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Control.PreviewMouseDoubleClickEvent };
                headword.RaiseEvent(click);
                Assert.True(click.Handled);
                Assert.Equal(word, headword.SelectedText);
                Assert.False(ReviewKeyboard.Handle(review, Key.C, ModifierKeys.Control, false, headword));
                Assert.True(ApplicationCommands.Copy.CanExecute(null, headword));
                copied = null;
                ApplicationCommands.Copy.Execute(null, headword);
                Assert.Equal(word, copied);
            }

            SelectAndCopy();
            Assert.False(review.IsAnswerVisible);
            Assert.True(ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false, headword));
            Assert.True(review.IsAnswerVisible);
            Layout(view, 959, 710);
            SelectAndCopy();
            Assert.Equal(word, review.Current!.Word.Headword);
            Assert.False(ApplicationCommands.Paste.CanExecute(null, headword));
            Assert.False(ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false, new TextBox()));
        });
    }
}
