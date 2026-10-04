using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Desktop.Views;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed partial class DesktopWorkflowTests
{
    [Theory]
    [InlineData(Key.D1, ReviewRating.Again)]
    [InlineData(Key.NumPad2, ReviewRating.Hard)]
    [InlineData(Key.D3, ReviewRating.Good)]
    [InlineData(Key.NumPad4, ReviewRating.Easy)]
    public async Task KeyboardRequiresFlipThenSubmitsTheChosenRatingOnce(Key key, ReviewRating rating)
    {
        var store = new RecordingStore { NextReview = new(StudyTestData.Word(), 7, true, null) };
        var submissions = new List<ReviewSubmission>();
        var release = new TaskCompletionSource<ReviewResult>();
        store.Submit = submission => { submissions.Add(submission); return release.Task; };
        var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
        await review.LoadAsync();
        Assert.True(ReviewKeyboard.Handle(review, key, ModifierKeys.None, false));
        Assert.Empty(submissions);
        Assert.True(ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false));
        Assert.True(review.IsAnswerVisible);
        Assert.True(ReviewKeyboard.Handle(review, key, ModifierKeys.None, false));
        Assert.Equal(rating, Assert.Single(submissions).Rating);
        Assert.True(ReviewKeyboard.Handle(review, key, ModifierKeys.None, true));
        Assert.True(ReviewKeyboard.Handle(review, key, ModifierKeys.None, false));
        Assert.Single(submissions);
        store.NextReview = null;
        release.SetResult(new(submissions[0].OperationId, DateTimeOffset.UtcNow.AddDays(1), "Review"));
        while (review.HasCard) await Task.Yield();
    }

    [Fact]
    public void CustomShortcutsRespectEditingModifiersAndClosedScopeComboBox()
    {
        OffscreenWpf.Invoke(() =>
        {
            var settings = new AppSettings { FlipKey = "F1", AgainKey = "J", HardKey = "K", GoodKey = "L", EasyKey = "0" };
            var store = new RecordingStore { NextReview = new(StudyTestData.Word(), 0, true, null) };
            ReviewSubmission? saved = null;
            store.Submit = submission =>
            {
                saved = submission;
                store.NextReview = null;
                return Task.FromResult(new ReviewResult(submission.OperationId, DateTimeOffset.UtcNow.AddDays(1), "Review"));
            };
            var review = new ReviewViewModel(store, new SilentSpeech(), settings);
            review.LoadAsync().GetAwaiter().GetResult();
            Assert.False(ReviewKeyboard.Handle(review, Key.F1, ModifierKeys.None, false, new TextBox()));
            Assert.False(ReviewKeyboard.Handle(review, Key.F1, ModifierKeys.None, false, new ComboBox { IsEditable = true }));
            Assert.False(ReviewKeyboard.Handle(review, Key.F1, ModifierKeys.Control, false));
            Assert.True(ReviewKeyboard.Handle(review, Key.F1, ModifierKeys.None, true));
            Assert.False(review.IsAnswerVisible);
            Assert.False(ReviewKeyboard.Handle(review, Key.Space, ModifierKeys.None, false));
            Assert.True(ReviewKeyboard.Handle(review, Key.F1, ModifierKeys.None, false, new ComboBox()));
            Assert.True(review.IsAnswerVisible);
            Assert.Contains("F1", review.FlipButtonText);
            Assert.True(ReviewKeyboard.Handle(review, Key.NumPad0, ModifierKeys.None, false));
            Assert.Equal(ReviewRating.Easy, saved!.Rating);
        });
    }

    [Fact]
    public async Task EveryOfferedShortcutCanBeUsedToFlipIncludingEnter()
    {
        foreach (var name in AppSettings.ShortcutKeys)
        {
            var ratings = AppSettings.ShortcutKeys.Where(x => x != name).Take(4).ToArray();
            var settings = new AppSettings { FlipKey = name, AgainKey = ratings[0], HardKey = ratings[1], GoodKey = ratings[2], EasyKey = ratings[3] };
            var store = new RecordingStore { NextReview = new(StudyTestData.Word(), 0, true, null) };
            var review = new ReviewViewModel(store, new SilentSpeech(), settings);
            await review.LoadAsync();
            var key = name.Length == 1 && char.IsDigit(name[0]) ? Key.D0 + (name[0] - '0') : Enum.Parse<Key>(name);
            Assert.True(ReviewKeyboard.Handle(review, key, ModifierKeys.None, false));
            Assert.True(review.IsAnswerVisible, name);
        }
    }

    [Fact]
    public async Task SettingsSaveLargeLimitsAndShortcutsWithoutChangingActiveSettingsOnFailure()
    {
        using var data = new StudyTestData();
        var settings = new AppSettings { Practice = new() { Mode = PracticeMode.Listening, Level = PracticeLevel.Hard, QuestionCount = 5 } };
        var viewModel = new SettingsViewModel(new BackupService(data.Store), new FixedGenerator(), new SilentSpeech(),
            settings, new AiSettings(), data.DirectoryPath, () => { });
        viewModel.DailyNewLimit = "99999999999999999999999999999999999";
        viewModel.FlipKey = "F1";
        viewModel.GoodKey = "J";
        viewModel.SpeakKey = "F7";
        viewModel.SpeakExampleKey = "F8";
        await viewModel.SaveCommand.ExecuteAsync();
        Assert.Equal("", viewModel.Error);
        Assert.Equal(viewModel.DailyNewLimit, settings.DailyNewLimit.ToString());
        Assert.Equal("F1", AppSettings.Load(data.DirectoryPath).FlipKey);
        Assert.Equal("F7", settings.SpeakKey);
        Assert.Equal("F8", AppSettings.Load(data.DirectoryPath).SpeakExampleKey);
        var savedPractice = AppSettings.Load(data.DirectoryPath).Practice;
        Assert.Equal(PracticeMode.Listening, savedPractice.Mode);
        Assert.Equal(PracticeLevel.Hard, savedPractice.Level);
        Assert.Equal(5, savedPractice.QuestionCount);
        viewModel.GoodKey = "F1";
        viewModel.DailyNewLimit = "20";
        await viewModel.SaveCommand.ExecuteAsync();
        Assert.Contains("不同", viewModel.Error);
        Assert.Equal("J", settings.GoodKey);
        Assert.Equal("99999999999999999999999999999999999", settings.DailyNewLimit.ToString());
    }

    [Fact]
    public void AllSeedCardsFitTheMinimumReviewPageWithFixedActionsWithoutScrolling()
    {
        OffscreenWpf.Invoke(() =>
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var seed = JsonSerializer.Deserialize<SeedPack>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "toeic-starter.json")), options)!;
            var store = new RecordingStore();
            var review = new ReviewViewModel(store, new SilentSpeech(), new AppSettings());
            var view = new ReviewView { DataContext = review, FontSize = 14, FontFamily = new("Segoe UI, Microsoft JhengHei UI"),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)), Foreground = (Brush)Application.Current.Resources["InkBrush"] };
            foreach (var word in seed.Items)
            {
                store.NextReview = new(word, 0, true, null);
                review.LoadAsync().GetAwaiter().GetResult();
                review.Notice = "已保存。下一次複習：10/04 08:00";
                Layout(view, 715, 560);
                AssertFits(view, "FlipCard");
                review.FlipCommand.Execute(null);
                Layout(view, 715, 560);
                foreach (var id in new[] { "ReviewHeadword", "ReviewMeaning", "RateAgain", "RateHard", "RateGood", "RateEasy" }) AssertFits(view, id);
                var text = Descendants(view).OfType<TextBlock>().Where(IsShown).Select(x => x.Text).ToArray();
                Assert.Contains(word.Meaning, text);
                foreach (var example in word.Examples)
                {
                    Assert.Contains(example.English, text);
                    Assert.Contains(example.Chinese, text);
                }
                foreach (var block in Descendants(view).OfType<TextBlock>().Where(IsShown)) AssertFits(view, block);
            }
            // The topic selector can scroll when there are many topics. Its container alone
            // does not mean the card needs scrolling; verify the visible layout instead.
            Assert.All(Descendants(view).OfType<ScrollViewer>().Where(IsShown), scroll =>
            {
                Assert.True(scroll.ScrollableHeight <= 0.1, "The minimum review page requires vertical scrolling.");
                Assert.True(scroll.ScrollableWidth <= 0.1, "The minimum review page requires horizontal scrolling.");
            });
            var contentHost = (FrameworkElement)view.FindName("ContentHost");
            Assert.DoesNotContain(Descendants(contentHost), x => x is ScrollViewer);
            Assert.All(Descendants(view).OfType<FrameworkElement>(), x => Assert.Null(x.ToolTip));
            var longest = seed.Items.MaxBy(x => x.Examples.Sum(e => e.English.Length + e.Chinese.Length))!;
            store.NextReview = new(longest, 0, true, null);
            review.LoadAsync().GetAwaiter().GetResult();
            review.FlipCommand.Execute(null);
            Layout(view, 715, 560);
            Render(view, "review-v014-minimum.png");
            Layout(view, 959, 710);
            Render(view, "review-v014-normal.png");
        });
    }

    private static void Layout(FrameworkElement view, double width, double height)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            view.Measure(new(width, height));
            view.Arrange(new(0, 0, width, height));
            view.UpdateLayout();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        DependencyObject? parent = element;
        while (parent is not null)
        {
            if (parent is UIElement { Visibility: not Visibility.Visible }) return false;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return element.ActualWidth > 0 && element.ActualHeight > 0;
    }

    private static void AssertFits(FrameworkElement view, string id) => AssertFits(view,
        Assert.Single(Descendants(view).OfType<FrameworkElement>(), x => AutomationProperties.GetAutomationId(x) == id));

    private static void AssertFits(FrameworkElement view, FrameworkElement element)
    {
        Assert.True(IsShown(element));
        var bounds = element.TransformToAncestor(view).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= view.ActualWidth + 1 && bounds.Bottom <= view.ActualHeight + 1,
            $"{element.GetType().Name} {AutomationProperties.GetAutomationId(element)} {bounds} exceeds {view.ActualWidth}x{view.ActualHeight}");
    }

    private static void Render(FrameworkElement view, string filename)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Wording.sln"))) root = root.Parent;
        if (root is null) return;
        var directory = Path.Combine(root.FullName, "artifacts", "layout-check");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, filename));
        encoder.Save(output);
    }
}

// Load app resources without running the production startup or accessing the user's database.
internal static class OffscreenWpf
{
    private static readonly Lazy<Dispatcher> DispatcherThread = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Wording;component/Styles.xaml", UriKind.Relative) });
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        }) { IsBackground = true, Name = "Wording offscreen tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    public static void Invoke(Action action) => DispatcherThread.Value.Invoke(action);
    public static Task InvokeAsync(Func<Task> action) => DispatcherThread.Value.InvokeAsync(action).Task.Unwrap();
}
