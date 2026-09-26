using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using WordTrail.Core;
using WordTrail.Infrastructure;
using Xunit;

namespace WordTrail.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task BackupRestorePreservesStableIdsReviewHistoryAndSchedule()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var operation = new ReviewSubmission(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion);
        var result = await data.Store.SubmitReviewAsync(operation);
        var backup = new BackupService(data.Store);
        var archive = Path.Combine(data.DirectoryPath, "saved.wtbackup");
        var cardId = data.Scalar("SELECT card_id FROM cards;");
        await backup.CreateBackupAsync(archive);
        await data.AddWordAsync("a later word");
        await data.Store.SetPausedAsync(word.Id, true);
        await backup.RestoreBackupAsync(archive);
        var restored = Assert.Single(await data.Store.GetVocabularyAsync());
        Assert.Equal(word.Id, restored.Id);
        Assert.False(restored.IsPaused);
        Assert.Equal(cardId, data.Scalar("SELECT card_id FROM cards;"));
        Assert.Equal(result.DueAt.ToUnixTimeMilliseconds(), data.Scalar("SELECT due_ms FROM cards;"));
        Assert.Equal(operation.OperationId.ToString("D"), data.Scalar("SELECT operation_id FROM review_log;"));
        Assert.Equal(1, (await data.Store.GetDashboardAsync(data.Clock.Now)).StartedToday);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(data.DirectoryPath, "backups"), "before-restore-*.wtbackup"));
        await Assert.ThrowsAsync<ReviewConflictException>(() => data.Store.UndoReviewAsync(operation.OperationId));

        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(["manifest.json", "study.sqlite"], zip.Entries.Select(x => x.FullName).Order().ToArray());
    }

    [Fact]
    public async Task BackupCanBeRestoredIntoAnotherInitializedDataDirectory()
    {
        using var original = new StudyTestData();
        using var destination = new StudyTestData();
        await original.Store.InitializeAsync();
        await destination.Store.InitializeAsync();
        var item = await original.AddWordAsync();
        var archive = Path.Combine(original.DirectoryPath, "portable.wtbackup");
        await new BackupService(original.Store).CreateBackupAsync(archive);
        await new BackupService(destination.Store).RestoreBackupAsync(archive);
        Assert.Equal(item.Id, Assert.Single(await destination.Store.GetVocabularyAsync()).Id);
    }

    [Theory]
    [InlineData("Restore.BeforeReplace")]
    [InlineData("Restore.AfterReplace")]
    public async Task RestoreFailurePreservesOriginalDatabase(string point)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        var service = new BackupService(data.Store);
        var archive = Path.Combine(data.DirectoryPath, "old.wtbackup");
        await service.CreateBackupAsync(archive);
        var later = await data.AddWordAsync("reservation");
        data.FailAt = point;
        await Assert.ThrowsAsync<IOException>(() => service.RestoreBackupAsync(archive));
        Assert.Equal(2, (await data.Store.GetVocabularyAsync()).Count);
        Assert.Contains(await data.Store.GetVocabularyAsync(), item => item.Id == later.Id);
        data.FailAt = null;
        await data.NewStore().InitializeAsync();
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM senses;"));
    }

    [Fact]
    public async Task FailedBackupPublicationKeepsExistingBackup()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        var service = new BackupService(data.Store);
        var archive = Path.Combine(data.DirectoryPath, "saved.wtbackup");
        await service.CreateBackupAsync(archive);
        var before = await File.ReadAllBytesAsync(archive);
        await data.AddWordAsync("later");
        data.FailAt = "Backup.BeforePublish";
        await Assert.ThrowsAsync<IOException>(() => service.CreateBackupAsync(archive));
        Assert.Equal(before, await File.ReadAllBytesAsync(archive));
        Assert.Empty(Directory.GetFiles(data.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task CancelledBackupDoesNotReplaceExistingDestination()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var archive = Path.Combine(data.DirectoryPath, "saved.wtbackup");
        await File.WriteAllTextAsync(archive, "existing");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BackupService(data.Store).CreateBackupAsync(archive, cancellation.Token));
        Assert.Equal("existing", await File.ReadAllTextAsync(archive));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("hash")]
    [InlineData("missing")]
    public async Task InvalidArchiveIsRejectedBeforeChangingUserData(string failure)
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var service = new BackupService(data.Store);
        var valid = Path.Combine(data.DirectoryPath, "valid.wtbackup");
        var invalid = Path.Combine(data.DirectoryPath, "invalid.wtbackup");
        await service.CreateBackupAsync(valid);
        RewriteArchive(valid, invalid, failure);
        await Assert.ThrowsAsync<StudyDataException>(() => service.RestoreBackupAsync(invalid));
        Assert.Equal(word.Id, Assert.Single(await data.Store.GetVocabularyAsync()).Id);
    }

    [Fact]
    public async Task PathTraversalArchiveIsRejectedWithoutExtractingOutsideStaging()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        await data.AddWordAsync();
        var archive = Path.Combine(data.DirectoryPath, "hostile.wtbackup");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.CreateEntry("../../escaped.txt");
            zip.CreateEntry("manifest.json");
        }
        await Assert.ThrowsAsync<StudyDataException>(() => new BackupService(data.Store).RestoreBackupAsync(archive));
        Assert.False(File.Exists(Path.Combine(data.DirectoryPath, "escaped.txt")));
        Assert.Single(await data.Store.GetVocabularyAsync());
    }

    [Fact]
    public async Task ConcurrentReviewAndBackupProducesAConsistentSnapshot()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = await data.AddWordAsync();
        var card = (await data.Store.GetNextReviewAsync(data.Clock.Now, 5))!;
        var archive = Path.Combine(data.DirectoryPath, "concurrent.wtbackup");
        await Task.WhenAll(
            data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), card.ScheduleVersion)),
            new BackupService(data.Store).CreateBackupAsync(archive));
        using var restored = new StudyTestData();
        await restored.Store.InitializeAsync();
        await new BackupService(restored.Store).RestoreBackupAsync(archive);
        var logs = Convert.ToInt64(restored.Scalar("SELECT COUNT(*) FROM review_log;"));
        var started = Convert.ToInt64(restored.Scalar("SELECT COUNT(*) FROM cards WHERE last_review_ms IS NOT NULL;"));
        Assert.Equal(logs, started);
    }

    private static void RewriteArchive(string inputPath, string outputPath, string failure)
    {
        using var input = ZipFile.OpenRead(inputPath);
        using var output = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        foreach (var entry in input.Entries)
        {
            if (failure == "missing" && entry.FullName == "study.sqlite") continue;
            var target = output.CreateEntry(entry.FullName);
            using var targetStream = target.Open();
            using var sourceStream = entry.Open();
            if (entry.FullName == "manifest.json")
            {
                var json = JsonNode.Parse(sourceStream)!;
                if (failure == "schema") json["schemaVersion"] = 999;
                if (failure == "hash") json["files"]![0]!["sha256"] = new string('0', 64);
                var bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
                targetStream.Write(bytes);
            }
            else sourceStream.CopyTo(targetStream);
        }
    }
}
