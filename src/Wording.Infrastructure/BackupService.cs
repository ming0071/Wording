using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Wording.Core;

namespace Wording.Infrastructure;

/// <summary>首版只備份資料庫與 manifest；Windows 合成語音不需要複製媒體或憑證。</summary>
public sealed class BackupService(SqliteStudyStore store) : IBackupService
{
    private const int FormatVersion = 1;
    private const long MaximumDatabaseBytes = 128L * 1024 * 1024;
    private const string DatabaseEntry = "study.sqlite";
    private const string ManifestEntry = "manifest.json";

    private sealed record BackupFile(string Name, long Length, string Sha256);
    private sealed record BackupManifest(string Application, int FormatVersion, int SchemaVersion,
        DateTimeOffset CreatedAt, BackupFile[] Files);

    public Task CreateBackupAsync(string destination, CancellationToken cancellationToken = default) =>
        store.WithMaintenanceAsync(() => CreateBackup(destination, cancellationToken), cancellationToken);

    public Task RestoreBackupAsync(string source, CancellationToken cancellationToken = default) =>
        store.WithMaintenanceAsync(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            var directory = Path.GetDirectoryName(store.DatabasePath)!;
            Directory.CreateDirectory(directory);
            var staging = Path.Combine(directory, ".restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var rollback = Path.Combine(directory, ".rollback-" + Guid.NewGuid().ToString("N") + ".sqlite");
            var replaced = false;
            try
            {
                var stagedDatabase = ValidateAndExtract(source, staging, cancellationToken);
                using (var validation = store.OpenConnection(path: stagedDatabase))
                {
                    SqliteStudyStore.UpgradeConnection(validation, () => store.InjectFault("Migration.BeforeCommit"));
                    SqliteStudyStore.ValidateConnection(validation);
                }
                cancellationToken.ThrowIfCancellationRequested();

                // 舊庫備份成功才往下走；這個檔案會保留，供使用者回到還原前狀態。
                var backupDirectory = Path.Combine(directory, "backups");
                Directory.CreateDirectory(backupDirectory);
                var beforeRestore = Path.Combine(backupDirectory,
                    $"before-restore-{store.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wtbackup");
                CreateBackup(beforeRestore, cancellationToken);

                // 所有連線都 Pooling=false 且在維護鎖內。轉成 DELETE 並關閉，避免舊 WAL 套到新庫。
                using (var connection = store.OpenConnection())
                {
                    var mode = SqliteStudyStore.Scalar(connection, null, "PRAGMA journal_mode=DELETE;") as string;
                    if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
                        throw new StudyDataException("資料庫仍被其他程式使用，無法還原。");
                }
                cancellationToken.ThrowIfCancellationRequested();
                store.InjectFault("Restore.BeforeReplace");
                File.Replace(stagedDatabase, store.DatabasePath, rollback);
                replaced = true;
                // 替換後不再接收取消；必須完成驗證，或回退舊庫。
                store.InjectFault("Restore.AfterReplace");
                using (var restored = store.OpenConnection())
                {
                    SqliteStudyStore.ValidateConnection(restored);
                    SqliteStudyStore.Execute(restored, null, "PRAGMA journal_mode=WAL;");
                }
                store.ClearUndo();
            }
            catch (Exception failure)
            {
                if (replaced && File.Exists(rollback))
                {
                    try
                    {
                        // 若驗證期間曾啟用 WAL，必須先收妥新庫的 sidecar，再放回舊庫。
                        if (File.Exists(store.DatabasePath + "-wal") || File.Exists(store.DatabasePath + "-shm"))
                        {
                            using var connection = store.OpenConnection();
                            var mode = SqliteStudyStore.Scalar(connection, null, "PRAGMA journal_mode=DELETE;") as string;
                            if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
                                throw new StudyDataException("新庫仍被占用，已保留 rollback 舊庫供手動復原。");
                        }
                        // 只替換本次生成、同目錄的 rollback 檔。
                        File.Replace(rollback, store.DatabasePath, null);
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new AggregateException(
                            $"還原失敗且無法自動回復。請勿新增資料；舊庫保留在 {rollback}，另有 before-restore 備份。",
                            failure, rollbackFailure);
                    }
                }
                throw;
            }
            finally
            {
                TryDeleteDirectory(staging);
                // 替換成功或回退完成後，rollback 已不再是唯一復原來源。
                // 若自動回退失敗則保留 rollback，絕不能在 finally 清掉。
            }
            TryDeleteFile(rollback);
        }, cancellationToken);

    private void CreateBackup(string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        destination = Path.GetFullPath(destination);
        if (new[] { store.DatabasePath, store.DatabasePath + "-wal", store.DatabasePath + "-shm" }
            .Contains(destination, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("備份位置不能是目前資料庫。");
        var targetDirectory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(targetDirectory);
        var staging = Path.Combine(targetDirectory, ".backup-" + Guid.NewGuid().ToString("N"));
        var temporaryArchive = Path.Combine(targetDirectory, ".backup-" + Guid.NewGuid().ToString("N") + ".tmp");
        Directory.CreateDirectory(staging);
        try
        {
            var snapshot = Path.Combine(staging, DatabaseEntry);
            using (var source = store.OpenConnection())
            using (var target = store.OpenConnection(path: snapshot))
                source.BackupDatabase(target);
            using (var validation = store.OpenConnection(readOnly: true, path: snapshot))
                SqliteStudyStore.ValidateConnection(validation);
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(snapshot);
            if (file.Length > MaximumDatabaseBytes) throw new StudyDataException("資料庫超過目前備份支援的 128 MB。");
            var manifest = new BackupManifest("Wording", FormatVersion, SqliteStudyStore.SchemaVersion,
                store.Now, [new(DatabaseEntry, file.Length, Hash(snapshot))]);
            using (var archive = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(snapshot, DatabaseEntry, CompressionLevel.Optimal);
                var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                using var output = entry.Open();
                JsonSerializer.Serialize(output, manifest, SqliteStudyStore.JsonOptions);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var verification = Path.Combine(staging, "verification");
            Directory.CreateDirectory(verification);
            ValidateAndExtract(temporaryArchive, verification, cancellationToken);
            store.InjectFault("Backup.BeforePublish");
            cancellationToken.ThrowIfCancellationRequested();
            // 使用者指定的既有備份只在新備份完整後才被取代。
            File.Move(temporaryArchive, destination, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryArchive);
            TryDeleteDirectory(staging);
        }
    }

    private static string ValidateAndExtract(string source, string staging, CancellationToken cancellationToken)
    {
        if (new FileInfo(source).Length > MaximumDatabaseBytes + 1024 * 1024)
            throw new StudyDataException("備份檔案過大。");
        using var archive = ZipFile.OpenRead(source);
        // 首版只有兩個允許的檔名；這也直接阻擋 ../、絕對路徑和重複 entry。
        if (archive.Entries.Count != 2 ||
            archive.Entries.Count(x => x.FullName == DatabaseEntry) != 1 ||
            archive.Entries.Count(x => x.FullName == ManifestEntry) != 1)
            throw new StudyDataException("備份包含不支援的檔案或路徑。");
        var manifestEntry = archive.GetEntry(ManifestEntry)!;
        var databaseEntry = archive.GetEntry(DatabaseEntry)!;
        if (manifestEntry.Length > 16384 || databaseEntry.Length > MaximumDatabaseBytes)
            throw new StudyDataException("备份內容超過大小限制。");
        BackupManifest manifest;
        using (var input = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<BackupManifest>(input, SqliteStudyStore.JsonOptions)
                ?? throw new StudyDataException("缺少備份資訊。");
        if (manifest.Application is not ("Wording" or "WordTrail") || manifest.FormatVersion != FormatVersion ||
            manifest.SchemaVersion < 1 || manifest.SchemaVersion > SqliteStudyStore.SchemaVersion || manifest.Files is null ||
            manifest.Files.Length != 1 || manifest.Files[0].Name != DatabaseEntry ||
            manifest.Files[0].Length != databaseEntry.Length)
            throw new StudyDataException("備份版本或檔案資訊不符合目前程式。");
        var destination = Path.Combine(staging, DatabaseEntry);
        using (var input = databaseEntry.Open())
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > MaximumDatabaseBytes || total > databaseEntry.Length)
                    throw new StudyDataException("備份解壓後超過宣告的大小。");
                output.Write(buffer, 0, read);
            }
            if (total != databaseEntry.Length) throw new StudyDataException("備份內容不完整。");
            output.Flush(flushToDisk: true);
        }
        if (!string.Equals(Hash(destination), manifest.Files[0].Sha256, StringComparison.OrdinalIgnoreCase))
            throw new StudyDataException("備份雜湊不符；檔案可能已損壞。");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
               { DataSource = destination, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            if (Convert.ToInt32(SqliteStudyStore.Scalar(connection, null, "PRAGMA user_version;")) != manifest.SchemaVersion)
                throw new StudyDataException("備份宣告的版本與資料庫不符。");
        }
        return destination;
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
