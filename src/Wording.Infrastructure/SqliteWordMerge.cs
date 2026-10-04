using System.Text.Json;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class SqliteStudyStore
{
    public Task<int> MergeDuplicateWordsAsync(CancellationToken token = default) => WithMaintenanceAsync(() =>
    {
        using var connection = OpenConnection();
        var groups = ReadWords(connection, null).GroupBy(x => x.WordId)
            .Where(x => x.Count() > 1).Select(x => x.OrderBy(w => w.CreationOrder).ToArray()).ToArray();
        if (groups.Length == 0) return 0;
        var merged = groups.Select(x => VocabularyMerge.Combine(x) with
        {
            Enrollment = Enrollment.Selected, IsPaused = false, IsArchived = x.All(w => w.IsArchived)
        }).ToArray();
        foreach (var word in merged) ValidateVocabulary(word);
        BackupBeforeWordMerge(token);
        using var transaction = connection.BeginTransaction();
        var mapping = new Dictionary<Guid, Guid>();
        for (var i = 0; i < groups.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var word = merged[i];
            foreach (var source in groups[i])
            {
                ClearWordLearning(connection, transaction, source.Id);
                mapping[source.Id] = word.Id;
                if (source.Id == word.Id) continue;
                Execute(connection, transaction, "DELETE FROM sense_categories WHERE sense_id=$id;", ("$id", Id(source.Id)));
                Execute(connection, transaction, "DELETE FROM cards WHERE sense_id=$id;", ("$id", Id(source.Id)));
                Execute(connection, transaction, "DELETE FROM senses WHERE id=$id;", ("$id", Id(source.Id)));
            }
            WriteContent(connection, transaction, VocabularyNotes.Clean(word), userEdited: true, preserveStar: false);
            Execute(connection, transaction, "UPDATE senses SET enrollment=1,paused=0,archived=$a WHERE id=$id;",
                ("$a", word.IsArchived ? 1 : 0), ("$id", Id(word.Id)));
            ResetWordSchedule(connection, transaction, word.Id);
            InjectFault("WordMerge.AfterGroup");
        }
        // Topic footprints stay intact; old target references now point to the combined word.
        var sessions = new List<(string Id, Guid[] Targets)>();
        using (var command = Command(connection, transaction, "SELECT id,targets_json FROM practice_sessions;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) sessions.Add((reader.GetString(0), JsonSerializer.Deserialize<Guid[]>(reader.GetString(1))!));
        foreach (var session in sessions)
            Execute(connection, transaction, "UPDATE practice_sessions SET targets_json=$t WHERE id=$id;",
                ("$t", JsonSerializer.Serialize(session.Targets.Select(x => mapping.GetValueOrDefault(x, x)).Distinct().ToArray())), ("$id", session.Id));
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        ClearUndo();
        return groups.Sum(x => x.Length - 1);
    }, token);

    private void BackupBeforeWordMerge(CancellationToken token) => BackupBeforeVocabularyMaintenance("word-merge", token);

    private void BackupBeforeVocabularyMaintenance(string reason, CancellationToken token) => new BackupService(this).CreateBackup(
        Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups", $"before-{reason}-{Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wtbackup"), token);

    private static void ClearWordLearning(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        Execute(connection, transaction, "DELETE FROM practice_ratings WHERE sense_id=$id;", ("$id", Id(id)));
        Execute(connection, transaction, "DELETE FROM review_log WHERE sense_id=$id;", ("$id", Id(id)));
        Execute(connection, transaction, "DELETE FROM new_starts WHERE sense_id=$id;", ("$id", Id(id)));
    }

    private void ResetWordSchedule(SqliteConnection connection, SqliteTransaction transaction, Guid id) =>
        Execute(connection, transaction,
            "UPDATE cards SET state=1,step=0,stability=NULL,difficulty=NULL,last_review_ms=NULL,due_ms=$now,version=version+1 WHERE sense_id=$id;",
            ("$now", Ms(Now)), ("$id", Id(id)));
}
