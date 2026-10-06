using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class SqliteStudyStore
{
    // Candidate is an active word in older databases; Skipped remains excluded until resumed.
    private const string ReviewEligibility = "s.enrollment<>2 AND s.archived=0 AND s.paused=0 ";
    private const string CategoryPredicate = "AND ($category IS NULL OR EXISTS(" +
        "SELECT 1 FROM sense_categories sc WHERE sc.sense_id=s.id AND sc.category IN (SELECT value FROM json_each($category)))) ";

    public Task<DashboardSummary> GetDashboardAsync(DateTimeOffset now, CancellationToken cancellationToken = default,
        string? category = null, IReadOnlyList<string>? categories = null) =>
        WithMaintenanceAsync(() =>
        {
            using var connection = OpenConnection();
            var day = LocalDay(now);
            var due = DueCount(connection, null, now, category, categories);
            var newCount = Count(connection, null,
                "SELECT COUNT(*) FROM cards c JOIN senses s ON s.id=c.sense_id " +
                "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NULL " + CategoryPredicate + ";",
                ("$category", NormalizeCategories(category, categories)));
            var reviewed = Count(connection, null,
                "SELECT COUNT(DISTINCT sense_id) FROM review_log WHERE local_day=$day AND undone=0;", ("$day", day));
            var started = StartedCount(connection, null, day);
            var total = Count(connection, null, "SELECT COUNT(*) FROM senses WHERE archived=0;");
            var next = Scalar(connection, null,
                "SELECT MIN(c.due_ms) FROM cards c JOIN senses s ON s.id=c.sense_id " +
                "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NOT NULL AND c.due_ms>$now " + CategoryPredicate + ";",
                ("$now", Ms(now)), ("$category", NormalizeCategories(category, categories)));
            return new DashboardSummary(due, newCount, reviewed, started, total,
                next is null or DBNull ? null : FromMs(Convert.ToInt64(next)));
        }, cancellationToken);

    public Task<ReviewItem?> GetNextReviewAsync(DateTimeOffset now, BigInteger dailyNewLimit,
        CancellationToken cancellationToken = default, string? category = null, IReadOnlyList<string>? categories = null) => WithMaintenanceAsync(() =>
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
                ("$now", Ms(now)), ("$category", NormalizeCategories(category, categories))) as string;
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
                    ("$space", hasSpace ? 1 : 0), ("$category", NormalizeCategories(category, categories))) as string;
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

    public Task<IReadOnlyList<StudyActivity>> GetStudyActivityAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default) => WithMaintenanceAsync<IReadOnlyList<StudyActivity>>(() =>
        {
            using var connection = OpenConnection();
            using var command = Command(connection, null,
                "SELECT local_day,COUNT(DISTINCT sense_id) FROM review_log WHERE undone=0 AND local_day BETWEEN $from AND $through GROUP BY local_day ORDER BY local_day;",
                ("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            using var reader = command.ExecuteReader();
            var result = new List<StudyActivity>();
            while (reader.Read()) result.Add(new(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetInt32(1)));
            return result;
        }, cancellationToken);

    public Task<ReviewResult> SubmitReviewAsync(ReviewSubmission submission,
        CancellationToken cancellationToken = default) => SubmitReviewInternalAsync(submission, null, null, cancellationToken);

    private Task<ReviewResult> SubmitReviewInternalAsync(ReviewSubmission submission, Guid? exerciseId,
        BigInteger? dailyLimit, CancellationToken cancellationToken) => WithMaintenanceAsync(() =>
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
                if (exerciseId is { } duplicateExercise && Scalar(connection, transaction,
                    "SELECT 1 FROM practice_ratings WHERE exercise_id=$e AND operation_id=$o;",
                    ("$e", Id(duplicateExercise)), ("$o", Id(submission.OperationId))) is null)
                    throw new ReviewConflictException("此評分操作不屬於本次練習。");
                return Result(submission.OperationId, duplicate.After);
            }
            if (exerciseId is { } exercise)
            {
                var completion = ReadPractice(connection, transaction, exercise)
                    ?? throw new ReviewConflictException("找不到已完成的練習，請重新開始。");
                if (!completion.TargetIds.Contains(submission.SenseId))
                    throw new ReviewConflictException("這個詞義不屬於本次練習。");
                if (Scalar(connection, transaction, "SELECT 1 FROM practice_ratings WHERE exercise_id=$e AND sense_id=$s;",
                    ("$e", Id(exercise)), ("$s", Id(submission.SenseId))) is not null)
                    throw new ReviewConflictException("這個詞義在本次練習已評分。");
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
            EnsureDay(connection, transaction, now, dailyLimit);
            if (before.LastReviewAt is null)
            {
                var wasStarted = Scalar(connection, transaction,
                    "SELECT 1 FROM new_starts WHERE sense_id=$id;", ("$id", Id(submission.SenseId))) is not null;
                // Practice may introduce extra words, but shares the same first-start accounting.
                if (exerciseId is null && !wasStarted && StartedCount(connection, transaction, day) >= DailyQuota(connection, transaction, day))
                    throw new ReviewConflictException("今天的新詞名額已用完；先完成到期複習，明天可繼續。");
                Execute(connection, transaction,
                    "INSERT OR IGNORE INTO new_starts(sense_id,first_day,started_ms) VALUES($id,$day,$now);",
                    ("$id", Id(submission.SenseId)), ("$day", day), ("$now", Ms(now)));
            }
            var after = _scheduler.Review(before, submission.Rating, now);
            WriteSchedule(connection, transaction, submission.SenseId, after, version);
            if (exerciseId is { } completedExercise)
                Execute(connection, transaction, "INSERT INTO practice_ratings(exercise_id,sense_id,operation_id) VALUES($e,$s,$o);",
                    ("$e", Id(completedExercise)), ("$s", Id(submission.SenseId)), ("$o", Id(submission.OperationId)));
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
            Execute(connection, transaction, "DELETE FROM practice_ratings WHERE operation_id=$op;", ("$op", Id(operationId)));
            transaction.Commit();
            _lastUndoOperation = null;
        }, cancellationToken);

    /// <summary>UI 離開複習頁時呼叫，撤銷能力不跨工作階段。</summary>
    public Task EndReviewSessionAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() => { _lastUndoOperation = null; }, cancellationToken);

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
        int? adaptive = ApplicationConfiguration.Current.Review.AdaptiveNewLimit(due);
        Execute(connection, transaction,
            "INSERT OR IGNORE INTO daily_limits(local_day,adaptive_limit,configured_limit) VALUES($day,$adaptive,$configured);",
            ("$day", LocalDay(now)), ("$adaptive", adaptive),
            ("$configured", (configuredLimit ?? BigInteger.Parse(ApplicationConfiguration.Current.Review.DailyNewLimit, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)));
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

    private static string? NormalizeCategories(string? category, IReadOnlyList<string>? categories)
    {
        var selected = (categories ?? (category is null ? [] : new[] { category }))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return selected.Length == 0 ? null : JsonSerializer.Serialize(selected);
    }

    private static int DueCount(SqliteConnection connection, SqliteTransaction? transaction, DateTimeOffset now,
        string? category = null, IReadOnlyList<string>? categories = null) =>
        Count(connection, transaction,
            "SELECT COUNT(*) FROM cards c JOIN senses s ON s.id=c.sense_id " +
            "WHERE " + ReviewEligibility + "AND c.last_review_ms IS NOT NULL AND c.due_ms<=$now " + CategoryPredicate + ";",
            ("$now", Ms(now)), ("$category", NormalizeCategories(category, categories)));

    private string LocalDay(DateTimeOffset now) =>
        TimeZoneInfo.ConvertTime(now, _timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
