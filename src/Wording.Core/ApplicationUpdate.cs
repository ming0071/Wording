namespace Wording.Core;

public sealed record ApplicationRelease(Version Version, string Notes, Uri Page,
    Uri Installer, string Sha256, long Size);
public sealed record DownloadedUpdate(ApplicationRelease Release, string InstallerPath);
public sealed record UpdateProgress(long Received, long Total)
{
    public double Percent => Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
}
public enum UpdatePhase { Idle, Checking, Available, Downloading, Ready, Installing, Current }

public interface IApplicationUpdateClient
{
    Task<ApplicationRelease?> CheckAsync(Version current, CancellationToken token = default);
    Task<DownloadedUpdate> DownloadAsync(ApplicationRelease release, string cacheDirectory,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default);
}

public interface IUpdateInstaller
{
    Task LaunchAsync(DownloadedUpdate update, string dataDirectory, CancellationToken token = default);
}
