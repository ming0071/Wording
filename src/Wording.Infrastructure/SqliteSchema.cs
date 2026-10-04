using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class SqliteStudyStore
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = OpenConnection();
            var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
            if (version > SchemaVersion)
                throw new StudyDataException("資料庫由較新版程式建立；請更新 Wording，資料未被修改。");
            if (version == 0)
            {
                var tables = Convert.ToInt32(Scalar(connection, null,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
                if (tables != 0) throw new StudyDataException("無法辨識既有資料庫，請使用備份還原。");
                using var transaction = connection.BeginTransaction();
                Execute(connection, transaction, SchemaSql);
                Execute(connection, transaction, PracticeSchemaSql);
                InjectFault("Migration.BeforeCommit");
                Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion};");
                transaction.Commit();
            }
            UpgradeConnection(connection, () => InjectFault("Migration.BeforeCommit"));
            PurgePractice(connection, null, _clock.GetUtcNow());
            ValidateConnection(connection);
            Execute(connection, null, "PRAGMA journal_mode=WAL;");
        }, cancellationToken);

    internal static void UpgradeConnection(SqliteConnection connection, Action? beforeCommit = null)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
        ValidateConnection(connection, allowLegacy: true);
        if (version == SchemaVersion) return;
        using var transaction = connection.BeginTransaction();
        if (version == 1) Execute(connection, transaction, """
            ALTER TABLE daily_limits RENAME TO daily_limits_v1;
            CREATE TABLE daily_limits(local_day TEXT NOT NULL PRIMARY KEY,
              adaptive_limit INTEGER CHECK(adaptive_limit IN(0,2)),
              configured_limit TEXT NOT NULL CHECK(length(configured_limit)>0 AND configured_limit NOT GLOB '*[^0-9]*'));
            INSERT INTO daily_limits SELECT local_day,
              CASE WHEN adaptive_limit IN(0,2) THEN adaptive_limit ELSE NULL END,
              CAST(configured_limit AS TEXT) FROM daily_limits_v1;
            DROP TABLE daily_limits_v1;
            """);
        Execute(connection, transaction, PracticeSchemaSql);
        beforeCommit?.Invoke();
        Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion};");
        transaction.Commit();
    }

    internal static void ValidateConnection(SqliteConnection connection, bool allowLegacy = false)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
        if (version != SchemaVersion && !(allowLegacy && version is 1 or 2 or 3))
            throw new StudyDataException("不支援此資料庫版本，原有資料未被修改。");
        if (!string.Equals(Scalar(connection, null, "PRAGMA integrity_check;") as string, "ok", StringComparison.Ordinal))
            throw new StudyDataException("資料庫完整性檢查失敗。");
        using (var command = Command(connection, null, "PRAGMA foreign_key_check;"))
        using (var reader = command.ExecuteReader())
            if (reader.Read()) throw new StudyDataException("資料庫含有無效的資料關聯。");
        // 也確認是本程式格式，不能只相信外來檔案的 user_version。
        Scalar(connection, null, "SELECT COUNT(*) FROM senses s JOIN cards c ON c.sense_id=s.id;");
        Scalar(connection, null, "SELECT COUNT(*) FROM review_log WHERE operation_id IS NOT NULL AND before_json IS NOT NULL;");
        using (var command = Command(connection, null, "SELECT adaptive_limit,configured_limit FROM daily_limits;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if ((!reader.IsDBNull(0) && (reader.GetInt32(0) < 0 || reader.GetInt32(0) > 10)) ||
                    !BigInteger.TryParse(reader.GetValue(1).ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    throw new StudyDataException("資料庫含有無效的新詞上限。");
        Scalar(connection, null, "SELECT COUNT(*) FROM new_starts WHERE first_day IS NOT NULL;");
        Scalar(connection, null, "SELECT COUNT(*) FROM sense_categories;");
        Scalar(connection, null, "SELECT COUNT(*) FROM content_packs WHERE version>0;");
        if (version >= 3)
        {
            using var command = Command(connection, null, "SELECT id,completed_ms,mode,topics_json,targets_json FROM practice_sessions;");
            using var reader = command.ExecuteReader();
            while (reader.Read()) DecodePractice(reader);
            if (Count(connection, null, "SELECT COUNT(*) FROM practice_ratings p LEFT JOIN review_log r ON p.operation_id=r.operation_id WHERE r.operation_id IS NULL OR r.undone<>0 OR r.sense_id<>p.sense_id;") != 0)
                throw new StudyDataException("練習評分與複習紀錄不一致。");
        }
        if (Count(connection, null, "SELECT COUNT(*) FROM senses s LEFT JOIN cards c ON c.sense_id=s.id WHERE c.sense_id IS NULL;") != 0)
            throw new StudyDataException("部分詞義缺少卡片排程。");
        if (Count(connection, null, "SELECT COUNT(*) FROM sqlite_master WHERE type IN('trigger','view');") != 0)
            throw new StudyDataException("資料庫含有非 Wording 建立的規則。");
        foreach (var word in ReadWords(connection, null))
        {
            ValidateVocabulary(word);
            var (schedule, _) = ReadSchedule(connection, null, word.Id);
            var invalid = schedule.CardId == Guid.Empty || !Enum.IsDefined(schedule.State) ||
                (schedule.State == LearningState.Review ? schedule.Step is not null : schedule.Step is null or < 0) ||
                (schedule.LastReviewAt is null && (schedule.State != LearningState.Learning ||
                    schedule.Stability is not null || schedule.Difficulty is not null)) ||
                (schedule.LastReviewAt is not null &&
                    (schedule.Stability is not { } stability || !double.IsFinite(stability) || stability <= 0 ||
                     schedule.Difficulty is not { } difficulty || !double.IsFinite(difficulty) || difficulty is < 1 or > 10));
            if (invalid) throw new StudyDataException("資料庫含有不完整的卡片排程。");
        }
    }

    private const string SchemaSql = """
        CREATE TABLE senses(
          id TEXT NOT NULL PRIMARY KEY, headword TEXT NOT NULL, content_json TEXT NOT NULL,
          enrollment INTEGER NOT NULL CHECK(enrollment BETWEEN 0 AND 2),
          archived INTEGER NOT NULL DEFAULT 0 CHECK(archived IN(0,1)),
          paused INTEGER NOT NULL DEFAULT 0 CHECK(paused IN(0,1)),
          user_edited INTEGER NOT NULL DEFAULT 0 CHECK(user_edited IN(0,1)), seed_pack TEXT);
        CREATE TABLE categories(name TEXT COLLATE NOCASE NOT NULL PRIMARY KEY);
        CREATE TABLE sense_categories(sense_id TEXT NOT NULL REFERENCES senses(id),
          category TEXT COLLATE NOCASE NOT NULL REFERENCES categories(name), PRIMARY KEY(sense_id,category));
        CREATE TABLE cards(sense_id TEXT NOT NULL PRIMARY KEY REFERENCES senses(id),
          card_id TEXT NOT NULL UNIQUE, state INTEGER NOT NULL CHECK(state BETWEEN 1 AND 3),
          step INTEGER, stability REAL, difficulty REAL, due_ms INTEGER NOT NULL,
          last_review_ms INTEGER, version INTEGER NOT NULL DEFAULT 0 CHECK(version>=0));
        CREATE INDEX ix_cards_due ON cards(due_ms);
        CREATE TABLE review_log(sequence INTEGER PRIMARY KEY AUTOINCREMENT,
          operation_id TEXT NOT NULL UNIQUE, sense_id TEXT NOT NULL REFERENCES senses(id),
          rating INTEGER NOT NULL CHECK(rating BETWEEN 1 AND 4), reviewed_ms INTEGER NOT NULL,
          local_day TEXT NOT NULL, before_json TEXT NOT NULL, after_json TEXT NOT NULL,
          expected_version INTEGER NOT NULL, result_version INTEGER NOT NULL,
          scheduler_version TEXT NOT NULL, undone INTEGER NOT NULL CHECK(undone IN(0,1)), session_id TEXT NOT NULL);
        CREATE INDEX ix_reviews_day ON review_log(local_day,undone);
        CREATE TABLE new_starts(sense_id TEXT NOT NULL PRIMARY KEY REFERENCES senses(id),
          first_day TEXT NOT NULL, started_ms INTEGER NOT NULL);
        CREATE TABLE daily_limits(local_day TEXT NOT NULL PRIMARY KEY,
          adaptive_limit INTEGER CHECK(adaptive_limit IN(0,2)),
          configured_limit TEXT NOT NULL CHECK(length(configured_limit)>0 AND configured_limit NOT GLOB '*[^0-9]*'));
        CREATE TABLE content_packs(pack_id TEXT NOT NULL PRIMARY KEY, version INTEGER NOT NULL);
        """;
}
