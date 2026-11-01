using System.Diagnostics;
using System.Security.Cryptography;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed class UpdateInstaller : IUpdateInstaller
{
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Wording");

    public async Task LaunchAsync(DownloadedUpdate update, string dataDirectory, CancellationToken token = default)
    {
        await using var stream = new FileStream(update.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (stream.Length != update.Release.Size || !hash.Equals(update.Release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("已下載的安裝檔已變動，請重新下載。");
        var info = new ProcessStartInfo(update.InstallerPath) { UseShellExecute = false };
        foreach (var argument in BuildArguments(Environment.ProcessId, dataDirectory)) info.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException("無法啟動安裝程式，原版本仍可使用。");
    }

    public static string[] BuildArguments(int parentPid, string dataDirectory) =>
    ["/SILENT", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS",
        "/WORDINGUPDATE=1", $"/WORDINGPARENTPID={parentPid}", $"/WORDINGDATADIR={Path.GetFullPath(dataDirectory)}"];
}
