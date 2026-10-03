using System.IO;
using System.Text.Json;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Desktop;

internal static class VocabularyFileCommand
{
    public static bool IsRequested(string[] args) => args.Contains("--import-vocabulary") || args.Contains("--export-vocabulary");

    public static async Task<int> ExecuteAsync(string[] args, IStudyStore store)
    {
        var importing = args.Contains("--import-vocabulary");
        if (importing && args.Contains("--export-vocabulary")) throw new ArgumentException("一次只能匯入或匯出。");
        var path = GetValue(args, importing ? "--import-vocabulary" : "--export-vocabulary");
        var service = new VocabularyFileService(store);
        var count = importing ? await service.ImportAsync(path) : await service.ExportAsync(path);
        WriteResult(args, true, $"已{(importing ? "匯入" : "匯出")} {count} 個詞義。", count);
        return 0;
    }

    public static void WriteResult(string[] args, bool success, string message, int count = 0)
    {
        var json = JsonSerializer.Serialize(new { success, message, count }, VocabularyFileService.JsonOptions);
        if (args.Contains("--result-file")) File.WriteAllText(GetValue(args, "--result-file"), json);
        else Console.WriteLine(json);
    }

    private static string GetValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} 後需要檔案路徑。");
        return Path.GetFullPath(args[index + 1]);
    }
}
