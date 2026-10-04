using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wording.Core;
using Wording.Desktop;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

// Documentation-only host: real views and SQLite, fixed original examples, no Codex calls.
// It never opens the normal application data directory. Each launch uses a fresh temporary library.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Match the product's App.xaml light Fluent theme.
#pragma warning disable WPF0001
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose, ThemeMode = ThemeMode.Light };
#pragma warning restore WPF0001
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Wording;component/Styles.xaml", UriKind.Relative) });
        app.Startup += async (_, _) =>
        {
            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "Wording-showcase", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var store = new SqliteStudyStore(Path.Combine(directory, "wording.db"));
                await store.InitializeAsync();
                var document = JsonSerializer.Deserialize<VocabularyDocument>(
                    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "toeic-vocabulary.json")), VocabularyFileService.JsonOptions)!;
                string[] targets = ["check out", "check in", "confirm", "itinerary", "receipt", "reservation"];
                string[] others = ["invoice", "recruit", "deadline", "shipment", "customer", "budget", "announce", "meeting"];
                var path = Path.Combine(directory, "words.json");
                var words = targets.Concat(others).Select(headword => document.Items.Single(x => x.Headword == headword) with
                {
                    Categories = targets.Contains(headword) ? ["旅遊與生活"] : document.Items.Single(x => x.Headword == headword).Categories,
                    IsStarred = headword is "check out" or "reservation" or "invoice"
                }).ToArray();
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new VocabularyDocument(1, words), VocabularyFileService.JsonOptions));
                await new VocabularyFileService(store).ImportAsync(path);
                var now = DateTimeOffset.Now;
                for (var i = 0; i < 18; i++)
                    await store.CompletePracticeAsync(new(Guid.NewGuid(), now.AddDays(-i * 3 - 1),
                        i % 3 == 0 ? PracticeMode.Listening : PracticeMode.Reading,
                        [i < 10 ? "旅遊與生活" : i < 15 ? "職場與人事" : "財務與投資"], [words[i % words.Length].Id]));
                var settings = new AppSettings { AutoSpeakWord = false, Practice = new() { Topics = ["旅遊與生活"] } };
                var speech = new ShowcaseSpeech();
                app.Exit += (_, _) => speech.Dispose();
                var model = new MainViewModel(store, new BackupService(store), new ShowcaseGenerator(), speech, settings, new(), directory);
                var window = new MainWindow { DataContext = model, Width = 1240, Height = 850 };
                // Window backgrounds are not part of the child visual exported by RenderTargetBitmap.
                ((Grid)window.Content).Background = window.Background;
                app.MainWindow = window;
                window.Show();
                await model.InitializeAsync();
                if (args.Length == 2 && args[0] == "--capture")
                {
                    await CaptureAsync(model, window, Path.GetFullPath(args[1]));
                    app.Shutdown();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), "Showcase failed");
                app.Shutdown(1);
            }
        };
        app.Run();
    }

    private static async Task CaptureAsync(MainViewModel model, MainWindow window, string output)
    {
        Directory.CreateDirectory(output);
        async Task Navigate(string name, PageViewModel page)
        {
            model.NavigateCommand.Execute(name);
            await Task.Delay(150);
            if (model.CurrentPage != page) throw new InvalidOperationException("Showcase navigation did not complete.");
            await page.LoadAsync();
            await Task.Delay(100);
        }
        void Save(string name)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2),
                (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth / 2d, bitmap.PixelHeight / 2d));
            bitmap.Render(background);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png"));
            encoder.Save(file);
        }
        await Navigate("library", model.Library);
        model.Library.SelectedSort = "單字：A–Z";
        await model.Library.LoadAsync();
        for (var i = 0; model.Library.IsBusy && i < 100; i++) await Task.Delay(20);
        if (model.Library.IsBusy) throw new InvalidOperationException("Showcase library did not finish loading.");
        Save("wording-library");
        await Navigate("review", model.Review);
        model.Review.FlipCommand.Execute(null);
        Save("wording-review");
        await Navigate("practice", model.Practice!);
        Save("wording-practice-setup");
        await model.Practice!.GenerateCommand.ExecuteAsync();
        if (!string.IsNullOrEmpty(model.Practice.Error)) throw new InvalidOperationException(model.Practice.Error);
        Save("wording-reading");
        int[] answers = [1, 0, 2, 3];
        for (var i = 0; i < model.Practice.Questions.Length; i++) model.Practice.Questions[i].SelectedIndex = answers[i];
        await model.Practice.SubmitCommand.ExecuteAsync();
        if (!string.IsNullOrEmpty(model.Practice.Error)) throw new InvalidOperationException(model.Practice.Error);
        Save("wording-results");
        var practice = Descendants(window).OfType<Wording.Desktop.Views.PracticeView>().Single();
        var questionScroll = (ScrollViewer)practice.FindName("QuestionScroll");
        var feedbackTitle = Descendants(questionScroll).OfType<TextBlock>().Single(x => x.Text == "這些單字，你原本就懂嗎？");
        questionScroll.ScrollToVerticalOffset(feedbackTitle.TransformToAncestor((FrameworkElement)questionScroll.Content).Transform(new Point()).Y);
        Save("wording-word-feedback");
        model.Practice.ListeningCommand.Execute(null);
        await model.Practice.GenerateCommand.ExecuteAsync();
        if (!string.IsNullOrEmpty(model.Practice.Error)) throw new InvalidOperationException(model.Practice.Error);
        ((ScrollViewer)practice.FindName("QuestionScroll")).ScrollToTop();
        Save("wording-listening");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

// Playback controls are illustrated without requiring a voice on the capture machine or emitting sound.
internal sealed class ShowcaseSpeech : IPronunciationService, IPracticeSpeech
{
    public bool IsAvailable => true;
    public string Status => "文件展示：無聲播放示範。";
    public PlaybackState Playback { get; private set; }
    public event Action? PlaybackChanged;
    public void Speak(string text) => Play(text, 0);
    public void Play(string text, int rate) => SetState(PlaybackState.Playing);
    public void Pause() => SetState(PlaybackState.Paused);
    public void Resume() => SetState(PlaybackState.Playing);
    public void Stop() => SetState(PlaybackState.Stopped);
    public void Dispose() => Stop();
    private void SetState(PlaybackState state) { Playback = state; PlaybackChanged?.Invoke(); }
}

internal sealed class ShowcaseGenerator : IContentGenerator, IPracticeGenerator
{
    public Task<string> CheckAvailabilityAsync(CancellationToken token = default) => Task.FromResult("文件展示：固定內容，不呼叫 Codex。");
    public Task<AiEnrichment> GenerateAsync(VocabularyItem word, CancellationToken token = default) =>
        throw new NotSupportedException("展示工具只提供閱讀與聽力範例。");

    public Task<PracticeMaterial> GeneratePracticeAsync(PracticeRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var listening = request.Options.Mode == PracticeMode.Listening;
        var passage = listening ? """
            Guest: Hello. I have a reservation for two nights. Could you confirm that breakfast is included?

            Receptionist: Certainly. Breakfast is served from seven to ten. You can check in at two this afternoon. If you arrive early, we can keep your bags at reception.

            Guest: That sounds great. My itinerary includes a meeting tomorrow morning. Is there a quiet place where I can prepare?

            Receptionist: Yes, our business lounge opens at six. There is no extra charge for hotel guests. When you check out, we will email your receipt. Please let us know whether you need a printed copy as well.

            Guest: An email is fine. One more thing: can I book a taxi to the station?

            Receptionist: Of course. Please tell us your departure time by the evening before your trip.
            """ : """
            Dear guests,

            Thank you for choosing Willow Hotel for your business trip. Please confirm your reservation before Friday so that we can prepare your room. You may check in from 2 p.m.; if you arrive earlier, our reception team can store your bags at no extra charge.

            We know that a busy itinerary leaves little time to relax. Our quiet business lounge opens at 6 a.m., and breakfast is served until 10 a.m. The lounge is available to all hotel guests, whether you are preparing for a meeting or catching up on email.

            When you check out, we will send your receipt by email. If you prefer a printed copy, simply ask at reception. Need a taxi to the station? Let us know your departure time the evening before, and we will arrange one for you.

            We look forward to welcoming you.
            The Willow Hotel Team
            """;
        PracticeQuestion[] questions = [
            new("detail", "When can guests check in?", ["At 6 a.m.", "From 2 p.m.", "Before Friday", "After 10 p.m."], 1,
                "文中明確說明下午兩點起可以辦理入住。", listening ? "You can check in at two this afternoon." : "You may check in from 2 p.m.",
                ["六點是商務休息室開放時間。", "符合原文的入住時間。", "週五是確認訂房的期限。", "文中沒有提到晚上十點。"]),
            new("purpose", "Why should guests share their departure time?", ["To arrange a taxi", "To extend breakfast hours", "To cancel a meeting", "To receive a discount"], 0,
                "飯店需要出發時間，才能安排前往車站的計程車。", listening ? "Please tell us your departure time by the evening before your trip." : "Let us know your departure time the evening before, and we will arrange one for you.",
                ["符合安排計程車的情境。", "早餐時間已固定。", "飯店不負責取消會議。", "文中未提到折扣。"]),
            new(listening ? "detail" : "vocabulary", "What does 'itinerary' refer to?", ["A hotel receipt", "A breakfast menu", "A travel schedule", "A room key"], 2,
                "itinerary 指旅行或出差的行程安排。", listening ? "My itinerary includes a meeting tomorrow morning." : "We know that a busy itinerary leaves little time to relax.",
                ["收據是 receipt。", "早餐菜單不是行程。", "行程安排符合文意。", "房卡是 room key。"]),
            new(listening ? "detail" : "comprehension", "How will guests receive their receipt?", ["By post", "Through a travel agent", "At the station", "By email"], 3,
                "飯店預設以電子郵件寄送收據；也可另外要求紙本。", listening ? "When you check out, we will email your receipt." : "When you check out, we will send your receipt by email.",
                ["文中未提及郵寄。", "收據由飯店提供。", "車站不是領取收據的地點。", "符合電子郵件寄送的說明。"])
        ];
        return Task.FromResult(new PracticeMaterial(listening ? "At the Hotel Reception" : "Your Stay at Willow Hotel", passage,
            "感謝您選擇 Willow Hotel。請於週五前確認訂房；下午兩點起可入住，提早抵達可以免費寄放行李。商務休息室清晨六點開放，早餐供應至十點。退房時飯店會寄送電子收據，也可以索取紙本。若需要到車站的計程車，請在前一晚告知出發時間。",
            questions.Take(request.Options.QuestionCount).ToArray(), request.Targets.Select(word => new PracticeUsage(word.Id, word.Headword, word.Meaning,
                passage.Split('\n', StringSplitOptions.RemoveEmptyEntries).First(line => line.Contains(word.Headword, StringComparison.OrdinalIgnoreCase)))).ToArray()));
    }
}
