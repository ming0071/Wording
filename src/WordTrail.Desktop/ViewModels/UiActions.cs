using System.Diagnostics;

namespace WordTrail.Desktop.ViewModels;

internal static class UiActions
{
    public static void OpenDictionary(string? word, Action<string> reportError)
    {
        if (string.IsNullOrWhiteSpace(word)) { reportError("請先輸入或選擇單字。"); return; }
        try
        {
            var url = "https://www.oxfordlearnersdictionaries.com/definition/english/" + Uri.EscapeDataString(word.Trim());
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) { reportError($"無法開啟辭典：{exception.Message}"); }
    }
}
