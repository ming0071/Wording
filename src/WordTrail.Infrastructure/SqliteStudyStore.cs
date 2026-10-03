using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WordTrail.Core;

namespace WordTrail.Infrastructure;

/// <summary>
/// 單人本機詞库：短交易、明確 SQL、一個維護鎖。
/// SQLite 的 async API 仍是同步 I/O，所以整個資料操作在背景執行緒完成。
/// </summary>
public sealed class SqliteStudyStore : IStudyStore
{
    internal const int SchemaVersion = 2;
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly FsrsScheduler _scheduler = new();
    private readonly Action<string>? _faultInjector;
    private readonly string _sessionId = Guid.NewGuid().ToString("D");
    private Guid? _lastUndoOperation;

    public string DatabasePath { get; }

    public SqliteStudyStore(string databasePath, TimeProvider? timeProvider = null,
        TimeZoneInfo? timeZone = null, Action<string>? faultInjector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _clock = timeProvider ?? TimeProvider.System;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _faultInjector = faultInjector;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = OpenConnection();
            var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
            if (version > SchemaVersion)
                throw new StudyDataException("資料庫由較新版程式建立；請更新 WordTrail，資料未被修改。");
            if (version == 0)
            {
                var tables = Convert.ToInt32(Scalar(connection, null,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
                if (tables != 0) throw new StudyDataException("無法辨識既有資料庫，請使用備份還原。");
                using var transaction = connection.BeginTransaction();
                Execute(connection, transaction, SchemaSql);
                InjectFault("Migration.BeforeCommit");
                Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion};");
                transaction.Commit();
            }
            UpgradeConnection(connection, () => InjectFault("Migration.BeforeCommit"));
            ValidateConnection(connection);
            Execute(connection, null, "PRAGMA journal_mode=WAL;");
        }, cancellationToken);

    public Task ImportSeedPackAsync(SeedPack pack, CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            ArgumentNullException.ThrowIfNull(pack);
            ArgumentException.ThrowIfNullOrWhiteSpace(pack.PackId);
            if (pack.Version < 1 || pack.Items is null || pack.Items.Length > 10000)
                throw new ArgumentException("資料包版本或項目數不正確。");
            if (pack.Items.Select(x => x.Id).Distinct().Count() != pack.Items.Length)
                throw new ArgumentException("資料包有重複的詞義 ID。");
            foreach (var item in pack.Items) ValidateVocabulary(item);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var imported = Scalar(connection, transaction,
                "SELECT version FROM content_packs WHERE pack_id=$id;", ("$id", pack.PackId));
            if (imported is not null && Convert.ToInt32(imported) >= pack.Version) return;
            foreach (var source in pack.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = ReadWord(connection, transaction, source.Id);
                var owner = Scalar(connection, transaction,
                    "SELECT seed_pack FROM senses WHERE id=$id;", ("$id", Id(source.Id)));
                if (existing is not null && !string.Equals(owner as string, pack.PackId, StringComparison.Ordinal))
                    throw new StudyDataException($"資料包與既有詞義 ID 衝突：{source.Headword}");
                if (existing is null)
                {
                    var item = Normalize(source) with
                    {
                        Enrollment = Enrollment.Selected, IsPaused = false,
                        IsArchived = false, IsUserEdited = false
                    };
                    InsertWord(connection, transaction, item, pack.PackId);
                }
                else if (!existing.IsUserEdited)
                {
                    // 更新教材內容，但絕不改動選詞、封存、暫停或複習排程。
                    WriteContent(connection, transaction, Normalize(source), userEdited: false);
                }
            }
            Execute(connection, transaction,
                "INSERT INTO content_packs(pack_id,version) VALUES($id,$v) " +
                "ON CONFLICT(pack_id) DO UPDATE SET version=excluded.version;",
                ("$id", pack.PackId), ("$v", pack.Version));
            transaction.Commit();
        }, cancellationToken);

    public Task<IReadOnlyList<VocabularyItem>> GetVocabularyAsync(string? search = null,
        string? category = null, CancellationToken cancellationToken = default, bool includeArchived = false) =>
        WithMaintenanceAsync<IReadOnlyList<VocabularyItem>>(() =>
        {
            using var connection = OpenConnection();
            var items = ReadWords(connection, null);
            var query = items.Where(x => includeArchived || !x.IsArchived);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                query = query.Where(x => x.Headword.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Meaning.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Collocations.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase)));
            }
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(x => x.Categories.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase));
            return query.OrderBy(x => x.Headword, StringComparer.OrdinalIgnoreCase).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync<IReadOnlyList<string>>(() =>
        {
            using var connection = OpenConnection();
            using var command = Command(connection, null, "SELECT name FROM categories ORDER BY name;");
            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read()) result.Add(reader.GetString(0));
            return result;
        }, cancellationToken);

    public Task AddCategoryAsync(string name, CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            ValidateCategory(name);
            using var connection = OpenConnection();
            Execute(connection, null, "INSERT OR IGNORE INTO categories(name) VALUES($name);", ("$name", name.Trim()));
        }, cancellationToken);

    public Task SaveVocabularyAsync(VocabularyItem item, CancellationToken cancellationToken = default) =>
        SaveVocabularyBatchAsync([item], cancellationToken);

    public Task SaveVocabularyBatchAsync(IReadOnlyList<VocabularyItem> items, CancellationToken cancellationToken = default)
    {
        var snapshot = items.ToArray();
        return WithMaintenanceAsync(() =>
        {
            if (snapshot.Length == 0 || snapshot.Select(x => x.Id).Distinct().Count() != snapshot.Length)
                throw new ArgumentException("請保存至少一個詞義，詞義 ID 不可重複。");
            foreach (var item in snapshot) ValidateVocabulary(item);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var source in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Normalize(source);
                var existing = ReadWord(connection, transaction, item.Id);
                if (existing is null)
                    InsertWord(connection, transaction, item with { IsUserEdited = true }, null);
                else
                {
                    WriteContent(connection, transaction, item, userEdited: true);
                    Execute(connection, transaction,
                        "UPDATE senses SET enrollment=$enrollment,archived=$archived,paused=$paused WHERE id=$id;",
                        ("$enrollment", (int)item.Enrollment), ("$archived", item.IsArchived ? 1 : 0),
                        ("$paused", item.IsPaused ? 1 : 0), ("$id", Id(item.Id)));
                    if (existing.Enrollment != item.Enrollment || existing.IsArchived != item.IsArchived || existing.IsPaused != item.IsPaused)
                        Execute(connection, transaction, "UPDATE cards SET version=version+1 WHERE sense_id=$id;", ("$id", Id(item.Id)));
                }
                InjectFault("Vocabulary.AfterItem");
            }
            transaction.Commit();
        }, cancellationToken);
    }

    public Task SetEnrollmentAsync(Guid senseId, Enrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(enrollment)) throw new ArgumentOutOfRangeException(nameof(enrollment));
        return ChangeFlagAsync(senseId, "enrollment", (int)enrollment, cancellationToken);
    }

    public Task SetPausedAsync(Guid senseId, bool paused, CancellationToken cancellationToken = default) =>
        ChangeFlagAsync(senseId, "paused", paused ? 1 : 0, cancellationToken);

    public Task ArchiveAsync(Guid senseId, CancellationToken cancellationToken = default) =>
        ChangeFlagAsync(senseId, "archived", 1, cancellationToken);

    private Task ChangeFlagAsync(Guid senseId, string column, int value, CancellationToken cancellationToken) =>
        WithMaintenanceAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (ReadWord(connection, transaction, senseId) is null) throw new StudyDataException("找不到詞義。");
            // column 僅來自上面三個固定入口，使用者文字一律走 SQL parameter。
            var changed = Execute(connection, transaction,
                $"UPDATE senses SET {column}=$value WHERE id=$id AND {column}<>$value;",
                ("$value", value), ("$id", Id(senseId)));
            if (changed > 0)
                Execute(connection, transaction, "UPDATE cards SET version=version+1 WHERE sense_id=$id;", ("$id", Id(senseId)));
            transaction.Commit();
        }, cancellationToken);

    // Candidate is an active word in older databases; Skipped remains excluded until resumed.
    private const string ReviewEligibility = "s.enrollment<>2 AND s.archived=0 AND s.paused=0 ";
    private const string CategoryPredicate = "AND ($category IS NULL OR EXISTS(" +
        "SELECT 1 FROM sense_categories sc WHERE sc.sense_id=s.id AND sc.category=$category)) ";

    public Task<DashboardSummary> GetDashboardAsync(DateTimeOffset now, CancellationToken cancellationToken = default,
        string? category = null) =>
        WithMaintenanceAsync(() =>
        {
            using var connection = OpenConnection();
            var day = LocalDay(now);
            var due = DueCount(connection, null, now, category);
            var newCount = Count(connection, null,
                "SELECT COUNT(*) FROM cards c JOIN senses s ON s.id=c.sense_id " +
                "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NULL " + CategoryPredicate + ";",
                ("$category", NormalizeCategory(category)));
            var reviewed = Count(connection, null,
                "SELECT COUNT(DISTINCT sense_id) FROM review_log WHERE local_day=$day AND undone=0;", ("$day", day));
            var started = StartedCount(connection, null, day);
            var total = Count(connection, null, "SELECT COUNT(*) FROM senses WHERE archived=0;");
            var next = Scalar(connection, null,
                "SELECT MIN(c.due_ms) FROM cards c JOIN senses s ON s.id=c.sense_id " +
                "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NOT NULL AND c.due_ms>$now " + CategoryPredicate + ";",
                ("$now", Ms(now)), ("$category", NormalizeCategory(category)));
            return new DashboardSummary(due, newCount, reviewed, started, total,
                next is null or DBNull ? null : FromMs(Convert.ToInt64(next)));
        }, cancellationToken);

    public Task<ReviewItem?> GetNextReviewAsync(DateTimeOffset now, BigInteger dailyNewLimit,
        CancellationToken cancellationToken = default, string? category = null) => WithMaintenanceAsync(() =>
        {
            if (dailyNewLimit < 0) throw new ArgumentOutOfRangeException(nameof(dailyNewLimit));
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var day = LocalDay(now);
            EnsureDay(connection, transaction, now, dailyNewLimit);
            var id = Scalar(connection, transaction,
                "SELECT c.sense_id FROM cards c JOIN senses s ON s.id=c.sense_id " +
                "WHERE " + ReviewEligibility + CategoryPredicate +
                "AND c.last_review_ms IS NOT NULL AND c.due_ms<=$now ORDER BY c.due_ms,s.rowid LIMIT 1;",
                ("$now", Ms(now)), ("$category", NormalizeCategory(category))) as string;
            if (id is null)
            {
                var quota = DailyQuota(connection, transaction, day);
                var hasSpace = StartedCount(connection, transaction, day) < quota;
                // 首次評分被撤銷的卡仍可重答；new_starts 不被 Undo 刪除，不能反覆換取名額。
                id = Scalar(connection, transaction,
                    "SELECT c.sense_id FROM cards c JOIN senses s ON s.id=c.sense_id " +
                    "WHERE " + ReviewEligibility + CategoryPredicate + "AND c.last_review_ms IS NULL " +
                    "AND ($space=1 OR EXISTS(SELECT 1 FROM new_starts n WHERE n.sense_id=s.id)) " +
                    "ORDER BY EXISTS(SELECT 1 FROM new_starts n WHERE n.sense_id=s.id) DESC,s.rowid LIMIT 1;",
                    ("$space", hasSpace ? 1 : 0), ("$category", NormalizeCategory(category))) as string;
            }
            ReviewItem? result = null;
            if (id is not null)
            {
                var senseId = Guid.Parse(id);
                var (schedule, version) = ReadSchedule(connection, transaction, senseId);
                result = new(ReadWord(connection, transaction, senseId)!, version,
                    schedule.LastReviewAt is null, schedule.LastReviewAt is null ? null : schedule.DueAt);
            }
            transaction.Commit();
            return result;
        }, cancellationToken);

    public Task<ReviewResult> SubmitReviewAsync(ReviewSubmission submission,
        CancellationToken cancellationToken = default) => WithMaintenanceAsync(() =>
        {
            if (submission.OperationId == Guid.Empty || !Enum.IsDefined(submission.Rating))
                throw new ArgumentException("評分操作不正確。");
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var duplicate = ReadLog(connection, transaction, submission.OperationId);
            if (duplicate is not null)
            {
                if (duplicate.SenseId != submission.SenseId || duplicate.Rating != submission.Rating ||
                    duplicate.ExpectedVersion != submission.ExpectedScheduleVersion ||
                    Ms(duplicate.ReviewedAt) != Ms(submission.ReviewedAt))
                    throw new ReviewConflictException("此操作 ID 已被不同的評分使用。");
                if (duplicate.Undone) throw new ReviewConflictException("此評分已撤銷，請重新顯示卡片再評分。");
                return Result(submission.OperationId, duplicate.After);
            }
            var word = ReadWord(connection, transaction, submission.SenseId)
                ?? throw new StudyDataException("找不到詞義。");
            if (word.IsArchived || word.IsPaused || word.Enrollment == Enrollment.Skipped)
                throw new ReviewConflictException("此卡已暫停或封存，請重新取得卡片。");
            var (before, version) = ReadSchedule(connection, transaction, submission.SenseId);
            if (version != submission.ExpectedScheduleVersion)
                throw new ReviewConflictException("這張卡已更新，請重新取得卡片後評分。");
            var now = FromMs(Ms(submission.ReviewedAt));
            if (before.LastReviewAt is { } last && now < last)
                throw new ReviewConflictException("電腦時間早於上次複習時間，資料未修改。");
            if (before.LastReviewAt is not null && before.DueAt > now)
                throw new ReviewConflictException("這張卡尚未到複習時間。");

            var day = LocalDay(now);
            EnsureDay(connection, transaction, now, null);
            if (before.LastReviewAt is null)
            {
                var wasStarted = Scalar(connection, transaction,
                    "SELECT 1 FROM new_starts WHERE sense_id=$id;", ("$id", Id(submission.SenseId))) is not null;
                if (!wasStarted && StartedCount(connection, transaction, day) >= DailyQuota(connection, transaction, day))
                    throw new ReviewConflictException("今天的新詞名額已用完；先完成到期複習，明天可繼續。");
                Execute(connection, transaction,
                    "INSERT OR IGNORE INTO new_starts(sense_id,first_day,started_ms) VALUES($id,$day,$now);",
                    ("$id", Id(submission.SenseId)), ("$day", day), ("$now", Ms(now)));
            }
            var after = _scheduler.Review(before, submission.Rating, now);
            WriteSchedule(connection, transaction, submission.SenseId, after, version);
            InjectFault("Review.AfterSchedule");
            Execute(connection, transaction,
                "INSERT INTO review_log(operation_id,sense_id,rating,reviewed_ms,local_day,before_json,after_json," +
                "expected_version,result_version,scheduler_version,undone,session_id) " +
                "VALUES($op,$id,$rating,$now,$day,$before,$after,$expected,$result,$scheduler,0,$session);",
                ("$op", Id(submission.OperationId)), ("$id", Id(submission.SenseId)),
                ("$rating", (int)submission.Rating), ("$now", Ms(now)), ("$day", day),
                ("$before", JsonSerializer.Serialize(before, JsonOptions)),
                ("$after", JsonSerializer.Serialize(after, JsonOptions)),
                ("$expected", version), ("$result", version + 1),
                ("$scheduler", FsrsScheduler.ParameterVersion), ("$session", _sessionId));
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            _lastUndoOperation = submission.OperationId;
            return Result(submission.OperationId, after);
        }, cancellationToken);

    public Task UndoReviewAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            if (_lastUndoOperation != operationId)
                throw new ReviewConflictException("只能撤銷目前工作階段最後一次評分。");
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var log = ReadLog(connection, transaction, operationId)
                ?? throw new ReviewConflictException("找不到可撤銷的評分。");
            var lastOperation = Scalar(connection, transaction,
                "SELECT operation_id FROM review_log ORDER BY sequence DESC LIMIT 1;") as string;
            var (_, currentVersion) = ReadSchedule(connection, transaction, log.SenseId);
            if (log.Undone || log.SessionId != _sessionId || lastOperation != Id(operationId) || currentVersion != log.ResultVersion)
                throw new ReviewConflictException("卡片已再次更新，無法撤銷這筆評分。");
            WriteSchedule(connection, transaction, log.SenseId, log.Before, currentVersion);
            InjectFault("Undo.AfterSchedule");
            Execute(connection, transaction, "UPDATE review_log SET undone=1 WHERE operation_id=$op;", ("$op", Id(operationId)));
            transaction.Commit();
            _lastUndoOperation = null;
        }, cancellationToken);

    /// <summary>UI 離開複習頁時呼叫，撤銷能力不跨工作階段。</summary>
    public Task EndReviewSessionAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() => { _lastUndoOperation = null; }, cancellationToken);

    internal async Task<T> WithMaintenanceAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return work();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal Task WithMaintenanceAsync(Action work, CancellationToken cancellationToken) =>
        WithMaintenanceAsync(() => { work(); return true; }, cancellationToken);

    internal void InjectFault(string point) => _faultInjector?.Invoke(point);
    internal void ClearUndo() => _lastUndoOperation = null;
    internal DateTimeOffset Now => _clock.GetUtcNow();

    internal SqliteConnection OpenConnection(bool readOnly = false, string? path = null)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, null, "PRAGMA trusted_schema=OFF;");
            if (!readOnly) Execute(connection, null, "PRAGMA synchronous=FULL;");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    internal static void UpgradeConnection(SqliteConnection connection, Action? beforeCommit = null)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
        ValidateConnection(connection, allowLegacy: true);
        if (version == SchemaVersion) return;
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            ALTER TABLE daily_limits RENAME TO daily_limits_v1;
            CREATE TABLE daily_limits(local_day TEXT NOT NULL PRIMARY KEY,
              adaptive_limit INTEGER CHECK(adaptive_limit IN(0,2)),
              configured_limit TEXT NOT NULL CHECK(length(configured_limit)>0 AND configured_limit NOT GLOB '*[^0-9]*'));
            INSERT INTO daily_limits SELECT local_day,
              CASE WHEN adaptive_limit IN(0,2) THEN adaptive_limit ELSE NULL END,
              CAST(configured_limit AS TEXT) FROM daily_limits_v1;
            DROP TABLE daily_limits_v1;
            """);
        beforeCommit?.Invoke();
        Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion};");
        transaction.Commit();
    }

    internal static void ValidateConnection(SqliteConnection connection, bool allowLegacy = false)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"));
        if (version != SchemaVersion && !(allowLegacy && version == 1))
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
        if (Count(connection, null, "SELECT COUNT(*) FROM senses s LEFT JOIN cards c ON c.sense_id=s.id WHERE c.sense_id IS NULL;") != 0)
            throw new StudyDataException("部分詞義缺少卡片排程。");
        if (Count(connection, null, "SELECT COUNT(*) FROM sqlite_master WHERE type IN('trigger','view');") != 0)
            throw new StudyDataException("資料庫含有非 WordTrail 建立的規則。");
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

    private void InsertWord(SqliteConnection connection, SqliteTransaction transaction, VocabularyItem item, string? packId)
    {
        Execute(connection, transaction,
            "INSERT INTO senses(id,headword,content_json,enrollment,archived,paused,user_edited,seed_pack) " +
            "VALUES($id,$word,$json,$enrollment,$archived,$paused,$edited,$pack);",
            ("$id", Id(item.Id)), ("$word", item.Headword), ("$json", JsonSerializer.Serialize(item, JsonOptions)),
            ("$enrollment", (int)item.Enrollment), ("$archived", item.IsArchived ? 1 : 0),
            ("$paused", item.IsPaused ? 1 : 0), ("$edited", item.IsUserEdited ? 1 : 0), ("$pack", packId));
        WriteCategories(connection, transaction, item.Id, item.Categories);
        var schedule = _scheduler.Create(Guid.NewGuid(), FromMs(Ms(_clock.GetUtcNow())));
        Execute(connection, transaction,
            "INSERT INTO cards(sense_id,card_id,state,step,due_ms,version) VALUES($id,$card,1,0,$due,0);",
            ("$id", Id(item.Id)), ("$card", Id(schedule.CardId)), ("$due", Ms(schedule.DueAt)));
    }

    private static void WriteContent(SqliteConnection connection, SqliteTransaction transaction,
        VocabularyItem item, bool userEdited)
    {
        Execute(connection, transaction,
            "UPDATE senses SET headword=$word,content_json=$json,user_edited=$edited WHERE id=$id;",
            ("$word", item.Headword), ("$json", JsonSerializer.Serialize(item, JsonOptions)),
            ("$edited", userEdited ? 1 : 0), ("$id", Id(item.Id)));
        WriteCategories(connection, transaction, item.Id, item.Categories);
    }

    private static void WriteCategories(SqliteConnection connection, SqliteTransaction transaction,
        Guid senseId, string[] categories)
    {
        Execute(connection, transaction, "DELETE FROM sense_categories WHERE sense_id=$id;", ("$id", Id(senseId)));
        foreach (var category in categories)
        {
            Execute(connection, transaction, "INSERT OR IGNORE INTO categories(name) VALUES($name);", ("$name", category));
            Execute(connection, transaction,
                "INSERT OR IGNORE INTO sense_categories(sense_id,category) VALUES($id,$name);",
                ("$id", Id(senseId)), ("$name", category));
        }
    }

    private static List<VocabularyItem> ReadWords(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction,
            "SELECT id,content_json,enrollment,archived,paused,user_edited FROM senses ORDER BY rowid;");
        using var reader = command.ExecuteReader();
        var result = new List<VocabularyItem>();
        while (reader.Read()) result.Add(DecodeWord(reader));
        return result;
    }

    private static VocabularyItem? ReadWord(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = Command(connection, transaction,
            "SELECT id,content_json,enrollment,archived,paused,user_edited FROM senses WHERE id=$id;", ("$id", Id(id)));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeWord(reader) : null;
    }

    private static VocabularyItem DecodeWord(SqliteDataReader reader)
    {
        var item = JsonSerializer.Deserialize<VocabularyItem>(reader.GetString(1), JsonOptions)
            ?? throw new StudyDataException("無法讀取詞義內容。");
        return item with { Id = Guid.Parse(reader.GetString(0)), Enrollment = (Enrollment)reader.GetInt32(2),
            IsArchived = reader.GetBoolean(3), IsPaused = reader.GetBoolean(4), IsUserEdited = reader.GetBoolean(5) };
    }

    private static (ReviewSchedule Schedule, long Version) ReadSchedule(SqliteConnection connection,
        SqliteTransaction? transaction, Guid senseId)
    {
        using var command = Command(connection, transaction,
            "SELECT card_id,state,step,stability,difficulty,due_ms,last_review_ms,version FROM cards WHERE sense_id=$id;",
            ("$id", Id(senseId)));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new StudyDataException("找不到卡片排程。");
        return (new ReviewSchedule(Guid.Parse(reader.GetString(0)), (LearningState)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetDouble(4), FromMs(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : FromMs(reader.GetInt64(6))), reader.GetInt64(7));
    }

    private static void WriteSchedule(SqliteConnection connection, SqliteTransaction transaction,
        Guid senseId, ReviewSchedule schedule, long expectedVersion)
    {
        var changed = Execute(connection, transaction,
            "UPDATE cards SET state=$state,step=$step,stability=$stability,difficulty=$difficulty," +
            "due_ms=$due,last_review_ms=$last,version=version+1 WHERE sense_id=$id AND version=$expected;",
            ("$state", (int)schedule.State), ("$step", schedule.Step), ("$stability", schedule.Stability),
            ("$difficulty", schedule.Difficulty), ("$due", Ms(schedule.DueAt)),
            ("$last", schedule.LastReviewAt is { } last ? Ms(last) : null),
            ("$id", Id(senseId)), ("$expected", expectedVersion));
        if (changed != 1) throw new ReviewConflictException("排程已改變，這次評分沒有寫入。");
    }

    private sealed record StoredLog(Guid SenseId, ReviewRating Rating, DateTimeOffset ReviewedAt,
        ReviewSchedule Before, ReviewSchedule After, long ExpectedVersion, long ResultVersion,
        bool Undone, string SessionId);

    private static StoredLog? ReadLog(SqliteConnection connection, SqliteTransaction transaction, Guid operationId)
    {
        using var command = Command(connection, transaction,
            "SELECT sense_id,rating,reviewed_ms,before_json,after_json,expected_version,result_version,undone,session_id " +
            "FROM review_log WHERE operation_id=$op;", ("$op", Id(operationId)));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(Guid.Parse(reader.GetString(0)), (ReviewRating)reader.GetInt32(1), FromMs(reader.GetInt64(2)),
            JsonSerializer.Deserialize<ReviewSchedule>(reader.GetString(3), JsonOptions)!,
            JsonSerializer.Deserialize<ReviewSchedule>(reader.GetString(4), JsonOptions)!,
            reader.GetInt64(5), reader.GetInt64(6), reader.GetBoolean(7), reader.GetString(8));
    }

    private void EnsureDay(SqliteConnection connection, SqliteTransaction transaction,
        DateTimeOffset now, BigInteger? configuredLimit)
    {
        var due = DueCount(connection, transaction, now);
        int? adaptive = due >= 20 ? 0 : due >= 10 ? 2 : null;
        Execute(connection, transaction,
            "INSERT OR IGNORE INTO daily_limits(local_day,adaptive_limit,configured_limit) VALUES($day,$adaptive,$configured);",
            ("$day", LocalDay(now)), ("$adaptive", adaptive),
            ("$configured", (configuredLimit ?? 5).ToString(CultureInfo.InvariantCulture)));
        if (configuredLimit is { } limit)
            Execute(connection, transaction, "UPDATE daily_limits SET configured_limit=$limit WHERE local_day=$day;",
                ("$limit", limit.ToString(CultureInfo.InvariantCulture)), ("$day", LocalDay(now)));
    }

    private static BigInteger DailyQuota(SqliteConnection connection, SqliteTransaction transaction, string day)
    {
        using var command = Command(connection, transaction,
            "SELECT adaptive_limit,configured_limit FROM daily_limits WHERE local_day=$day;", ("$day", day));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new StudyDataException("缺少今日新詞設定。");
        var configured = BigInteger.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        return reader.IsDBNull(0) ? configured : BigInteger.Min(configured, reader.GetInt32(0));
    }

    private static int StartedCount(SqliteConnection connection, SqliteTransaction? transaction, string day) =>
        Count(connection, transaction, "SELECT COUNT(*) FROM new_starts WHERE first_day=$day;", ("$day", day));

    private static string? NormalizeCategory(string? category) => string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    private static int DueCount(SqliteConnection connection, SqliteTransaction? transaction, DateTimeOffset now,
        string? category = null) =>
        Count(connection, transaction,
            "SELECT COUNT(*) FROM cards c JOIN senses s ON s.id=c.sense_id " +
            "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NOT NULL AND c.due_ms<=$now " + CategoryPredicate + ";",
            ("$now", Ms(now)), ("$category", NormalizeCategory(category)));

    private string LocalDay(DateTimeOffset now) =>
        TimeZoneInfo.ConvertTime(now, _timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static string Id(Guid id) => id.ToString("D");
    internal static long Ms(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    internal static DateTimeOffset FromMs(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
    private static ReviewResult Result(Guid operation, ReviewSchedule schedule) =>
        new(operation, schedule.DueAt, schedule.State.ToString());

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    internal static int Execute(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    internal static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return command.ExecuteScalar();
    }

    private static int Count(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt32(Scalar(connection, transaction, sql, parameters));

    private static VocabularyItem Normalize(VocabularyItem item) => item with
    {
        Headword = item.Headword.Trim(), Meaning = item.Meaning.Trim(), PartOfSpeech = item.PartOfSpeech.Trim(),
        Categories = item.Categories.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Collocations = item.Collocations.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(),
        Synonyms = item.Synonyms.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Notes = item.Notes.Trim()
    };

    private static void ValidateVocabulary(VocabularyItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Headword) || item.Headword.Length > 200 ||
            string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000 ||
            item.PartOfSpeech is null || item.Cue is null || item.Level is null || item.Kind is null ||
            item.Categories is null || item.Collocations is null || item.Synonyms is null || item.Notes is null || item.Notes.Length > 4000 || item.Examples is null || item.Origin is null ||
            !Enum.IsDefined(item.Enrollment))
            throw new ArgumentException("請填寫詞條與指定詞義，並確認欄位長度。");
        if (item.Categories.Length > 30 || item.Examples.Length > 20 || item.Collocations.Length > 30 || item.Synonyms.Length > 30)
            throw new ArgumentException("分類、例句或搭配數量過多。");
        foreach (var category in item.Categories) ValidateCategory(category);
        if (item.Collocations.Any(x => x is null || x.Length > 500) || item.Synonyms.Any(x => x is null || x.Length > 200) ||
            item.Examples.Any(x => x is null || string.IsNullOrWhiteSpace(x.English) ||
                x.English.Length > 4000 || x.Chinese is null || x.Chinese.Length > 4000))
            throw new ArgumentException("例句或搭配內容不正確。");
    }

    private static void ValidateCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new ArgumentException("分類名稱須為 1–80 個字。");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
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
