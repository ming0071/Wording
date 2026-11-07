using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        if (e.PropertyName == nameof(TranslationViewModel.FeedbackRevision) && sender is TranslationViewModel assessed)
        {
            var questions = assessed.Questions;
            var revision = assessed.FeedbackRevision;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (DataContext != assessed || assessed.Questions != questions || assessed.FeedbackRevision != revision ||
                    questions.FirstOrDefault()?.HasFeedback != true) return;
                ExerciseScroll.UpdateLayout();
                if (QuestionList.ItemContainerGenerator.ContainerFromIndex(0) is ContentPresenter presenter &&
                    QuestionList.ItemTemplate.FindName("FeedbackPanel", presenter) is FrameworkElement feedback)
                    ExerciseScroll.ScrollToVerticalOffset(Math.Max(0, feedback.TranslatePoint(new Point(), (UIElement)ExerciseScroll.Content).Y - 8));
            }));
        }
    }
}
