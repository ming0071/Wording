using System.IO;

namespace Wording.Desktop;

internal static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static string? logPath;

    public static void Initialize(string dataDirectory)
    {
        logPath = Path.Combine(dataDirectory, "diagnostics.log");
        Write($"Startup pid={Environment.ProcessId}; version={typeof(App).Assembly.GetName().Version}; dataDirectory={dataDirectory}");
    }

    public static void Write(string message)
    {
        if (logPath is null) return;
        try
        {
            lock (Gate)
                File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
