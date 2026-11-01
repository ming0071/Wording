using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wording.Desktop.ViewModels;
using Wording.Infrastructure;

namespace Wording.Desktop.Views;

public partial class ReadingBrowserHost : UserControl
{
    private const string Origin = "https://wording-reading.local";
    private ReadingPracticeViewModel? model;
    private CancellationTokenSource? lifetime;
    private CodexReadingSession? session;
    private bool recordingRequested, pageReady;
    private int connection;
    private bool wasReady;
    private int reconnectAttempts;
    private long connectedAt;
    private Task cleanup = Task.CompletedTask;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ReadingBrowserHost()
    {
        InitializeComponent();
        Loaded += Initialize;
        Unloaded += async (_, _) => await CloseAsync();
    }
    private async void Initialize(object sender, RoutedEventArgs e)
    {
        if (lifetime is not null || DataContext is not ReadingPracticeViewModel vm) return;
        model = vm; lifetime = new(); vm.Connecting(); vm.Requested += Requested;
        var initialization = lifetime;
        try
        {
            // A cached review view can be loaded again after its previous browser was disposed.
            Browser = new WebView2 { Focusable = false };
            System.Windows.Input.KeyboardNavigation.SetIsTabStop(Browser, false);
            Content = Browser;
            Browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(vm.Directory, "ReadingBrowser"),
                // Playback is explicitly triggered by a native WPF button/shortcut, outside the web document.
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
            };
            await Browser.EnsureCoreWebView2Async();
            if (lifetime != initialization) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping("wording-reading.local", Path.Combine(AppContext.BaseDirectory, "Reading"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => args.Cancel = !IsLocal(args.Uri);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = recordingRequested && IsLocal(args.Uri) &&
                args.PermissionKind == CoreWebView2PermissionKind.Microphone ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += MessageReceived;
            core.Navigate(Origin + "/index.html");
        }
        catch (Exception error) { if (lifetime == initialization) await FailAsync("語音介面無法啟動：" + error.Message); }
    }
    private static bool IsLocal(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "wording-reading.local" && uri.IsDefaultPort;
    private void Post(object value)
    {
        if (lifetime is not null && pageReady) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, Json));
    }
    private async void Requested(string action, int run)
    {
        try
        {
            if (action == "warm") { reconnectAttempts = 0; await WarmAsync(); return; }
            if (action == "record")
            {
                if (session is null) throw new InvalidOperationException("語音連線尚未準備完成。");
                recordingRequested = true; session.BeginUtterance();
            }
            Post(new { type = action, run });
        }
        catch (Exception error) { await FailAsync(error.Message); }
    }
    private async Task WarmAsync()
    {
        ++connection; recordingRequested = false;
        Post(new { type = "close" });
        await EndSessionAsync();
        if (lifetime is not null) Post(new { type = "warm", connection });
    }
    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (lifetime is null || model is null || !IsLocal(args.Source) || args.WebMessageAsJson.Length > 120_000) return;
        var receiving = lifetime;
        var receivingConnection = connection;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var message = document.RootElement;
            var type = message.GetProperty("type").GetString();
            if (type == "ready") { pageReady = true; await WarmAsync(); return; }
            if (!message.TryGetProperty("connection", out var id) || id.GetInt32() != connection) return;
            if (type == "offer" && session is null)
            {
                var generation = connection;
                var current = new CodexReadingSession(model.Settings, Path.Combine(model.Directory, "ReadingWorkspace"));
                session = current;
                current.TranscriptChanged += text =>
                {
                    var run = model?.Run ?? -1;
                    Dispatcher.BeginInvoke(() => { if (session == current) model?.Transcript(run, text); });
                };
                current.Failed += error => Dispatcher.BeginInvoke(async () =>
                { if (session == current) await FailAsync(error); });
                var answer = await current.StartAsync(message.GetProperty("sdp").GetString()!, lifetime.Token);
                if (session == current) Post(new { type = "answer", connection = generation, sdp = answer });
            }
            else if (type == "connected") { wasReady = true; connectedAt = Stopwatch.GetTimestamp(); model.Ready(); }
            else if (type == "connectionInterrupted") model.ConnectionInterrupted(true);
            else if (type == "connectionRecovered") model.ConnectionInterrupted(false);
            else if (type == "error") await FailAsync(message.GetProperty("message").GetString() ?? "語音連線中斷。");
            else if (message.TryGetProperty("run", out var runNumber))
            {
                var run = runNumber.GetInt32();
                if (type == "recording") model.Recording(run);
                else if (type == "finish" && run == model.Run && session is { } current)
                {
                    recordingRequested = false;
                    // The 30-second limit can finish without a keyboard command.
                    if (run == model.Run && model.Phase == ReadingPhase.Recording) model.RecordCommand.Execute(null);
                    var text = await current.FinishAsync(lifetime.Token);
                    if (session == current) { model.Complete(run, text); Post(new { type = "finished", run }); }
                }
                else if (type == "cancel" && session is { } cancelling)
                {
                    recordingRequested = false;
                    await cancelling.CancelUtteranceAsync(lifetime.Token);
                    if (session == cancelling) model.Cancelled(run);
                }
                else if (type == "playbackError") model.Fail("錄音無法回放，請重新連線後再試。");
            }
        }
        catch (OperationCanceledException) when (receiving.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (lifetime == receiving && connection == receivingConnection) await FailAsync("朗讀辨識未完成：" + error.Message);
        }
    }
    private async Task FailAsync(string message)
    {
        if (lifetime is null || model is null) return;
        var activeLifetime = lifetime;
        var retryToken = activeLifetime.Token;
        if (connectedAt != 0 && Stopwatch.GetElapsedTime(connectedAt).TotalSeconds >= 30) reconnectAttempts = 0;
        connectedAt = 0;
        var retry = wasReady && !recordingRequested && (model.Phase is ReadingPhase.Ready or ReadingPhase.Connecting) && reconnectAttempts < 3;
        var delaySeconds = retry ? new[] { 1, 3, 8 }[reconnectAttempts++] : 0;
        recordingRequested = false; ++connection;
        var failedConnection = connection;
        Post(new { type = "close" }); model?.Fail(message);
        if (retry) model?.Connecting($"語音連線中斷，正在自動重連（{reconnectAttempts}/3）…");
        await EndSessionAsync();
        if (retry)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), retryToken); }
            catch (OperationCanceledException) { return; }
            if (lifetime == activeLifetime && connection == failedConnection) await WarmAsync();
        }
    }
    private async Task EndSessionAsync()
    {
        var previous = session; session = null;
        if (previous is not null) cleanup = DisposeSessionAsync(previous);
        await cleanup;
    }
    private static async Task DisposeSessionAsync(CodexReadingSession value)
    {
        try { await value.DisposeAsync(); } catch (Exception) { /* Connection cleanup must not interrupt review/navigation. */ }
    }
    private async Task CloseAsync()
    {
        var previous = lifetime; lifetime = null; ++connection; recordingRequested = false; pageReady = false;
        wasReady = false; reconnectAttempts = 0; connectedAt = 0;
        if (model is not null) { model.Requested -= Requested; model.Disconnected(); model = null; }
        previous?.Cancel(); Browser.Dispose(); await EndSessionAsync(); previous?.Dispose();
    }
}
