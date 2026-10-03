using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WordTrail.Desktop.Views;

public partial class DictionaryBrowser : UserControl
{
    public static readonly DependencyProperty SourceUriProperty = DependencyProperty.Register(nameof(SourceUri), typeof(Uri),
        typeof(DictionaryBrowser), new PropertyMetadata(null, (d, _) => ((DictionaryBrowser)d).Navigate()));
    public Uri? SourceUri { get => (Uri?)GetValue(SourceUriProperty); set => SetValue(SourceUriProperty, value); }
    public string UserDataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordTrail", "DictionaryBrowser");
    private WebView2? browser;
    private Task? initialization;

    public DictionaryBrowser()
    {
        InitializeComponent();
        Loaded += (_, _) => Navigate();
        Unloaded += (_, _) => { var previous = browser; browser = null; initialization = null; BrowserHost.Children.Clear(); previous?.Dispose(); };
    }

    private async void Navigate()
    {
        if (!IsLoaded || SourceUri is null || PresentationSource.FromVisual(this) is null) return;
        WebView2? expected = null;
        try
        {
            Status.Text = "正在載入字典…";
            if (browser is null)
            {
                browser = new WebView2 { CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = UserDataFolder
                }};
                BrowserHost.Children.Add(browser);
                initialization = InitializeBrowserAsync(browser);
            }
            var current = browser;
            expected = current;
            await initialization!;
            if (browser != current || !IsLoaded) return;
            current.CoreWebView2.Navigate(SourceUri.AbsoluteUri);
        }
        catch (Exception)
        {
            if (expected is not null && expected != browser) return;
            if (IsLoaded) Status.Text = "內嵌字典未能啟動，請確認 Microsoft Edge WebView2 Runtime 已安裝；也可用下方按鈕開啟字典。";
            var failed = browser; browser = null; initialization = null;
            BrowserHost.Children.Clear(); failed?.Dispose();
        }
    }

    private async Task InitializeBrowserAsync(WebView2 view)
    {
        await view.EnsureCoreWebView2Async();
        view.CoreWebView2.Settings.AreDevToolsEnabled = false;
        view.CoreWebView2.Settings.IsStatusBarEnabled = false;
        view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        view.CoreWebView2.Settings.IsWebMessageEnabled = false;
        view.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) e.Cancel = true;
        };
        view.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) view.CoreWebView2.Navigate(uri.AbsoluteUri);
        };
        view.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
        view.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        view.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (view != browser) return;
            Status.Text = e.IsSuccess && e.HttpStatusCode < 400 ? "" : "字典網站目前無法載入；可切換字典、重試搜尋，或在瀏覽器開啟。";
        };
    }

    private void OpenExternal(object sender, RoutedEventArgs e)
    {
        if (SourceUri is null) return;
        try { Process.Start(new ProcessStartInfo(SourceUri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { Status.Text = "無法開啟預設瀏覽器，請稍後重試。"; }
    }
}
