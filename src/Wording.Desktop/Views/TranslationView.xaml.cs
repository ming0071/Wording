using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wording.Desktop.ViewModels;

namespace Wording.Desktop.Views;

public partial class TranslationView : UserControl
{
    private TranslationQuestionViewModel[]? displayedQuestions;
    public TranslationView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TranslationViewModel previous)
                PropertyChangedEventManager.RemoveHandler(previous, OnStateChanged, string.Empty);
            if (e.NewValue is TranslationViewModel current)
            {
                displayedQuestions = current.Questions;
                PropertyChangedEventManager.AddHandler(current, OnStateChanged, string.Empty);
            }
        };
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationViewModel.Questions) && sender is TranslationViewModel current &&
            !ReferenceEquals(displayedQuestions, current.Questions))
        {
            displayedQuestions = current.Questions;
            ExerciseScroll.ScrollToTop();
        }
    }
}
