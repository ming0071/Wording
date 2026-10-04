using System.Numerics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class SqliteStudyStore
{
    private const string PracticeSchemaSql = """
        CREATE TABLE IF NOT EXISTS practice_sessions(
          id TEXT NOT NULL PRIMARY KEY,completed_ms INTEGER NOT NULL,mode INTEGER NOT NULL CHECK(mode IN(0,1)),
          topics_json TEXT NOT NULL,targets_json TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_practice_completed ON practice_sessions(completed_ms);
        CREATE TABLE IF NOT EXISTS practice_ratings(
          exercise_id TEXT NOT NULL REFERENCES practice_sessions(id) ON DELETE CASCADE,
          sense_id TEXT NOT NULL REFERENCES senses(id),operation_id TEXT NOT NULL UNIQUE,
          PRIMARY KEY(exercise_id,sense_id));
        """;

    public Task<IReadOnlyList<PracticeCandidate>> GetPracticeCandidatesAsync(CancellationToken token = default) =>
        WithMaintenanceAsync<IReadOnlyList<PracticeCandidate>>(() =>
        {
            using var connection = OpenConnection();
            return ReadWords(connection, null).Where(x => !x.IsArchived && !x.IsPaused && x.Enrollment != Enrollment.Skipped)
                .Select(x => new PracticeCandidate(x, ReadSchedule(connection, null, x.Id).Schedule)).ToArray();
        }, token);

    public Task<IReadOnlyList<PracticeCompletion>> GetPracticeHistoryAsync(DateTimeOffset now, CancellationToken token = default) =>
        WithMaintenanceAsync<IReadOnlyList<PracticeCompletion>>(() =>
        {
            using var connection = OpenConnection();
            PurgePractice(connection, null, now);
            using var command = Command(connection, null, "SELECT id,completed_ms,mode,topics_json,targets_json FROM practice_sessions ORDER BY completed_ms DESC;");
            using var reader = command.ExecuteReader();
            var history = new List<PracticeCompletion>();
            while (reader.Read()) history.Add(DecodePractice(reader));
            return history;
        }, token);

    public Task CompletePracticeAsync(PracticeCompletion completion, CancellationToken token = default) =>
        WithMaintenanceAsync(() =>
        {
            ValidatePractice(completion);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            PurgePractice(connection, transaction, _clock.GetUtcNow());
            var existing = ReadPractice(connection, transaction, completion.Id);
            if (existing is not null)
            {
                if (Ms(existing.CompletedAt) != Ms(completion.CompletedAt) || existing.Mode != completion.Mode ||
                    !existing.Topics.SequenceEqual(completion.Topics) || !existing.TargetIds.SequenceEqual(completion.TargetIds))
                    throw new ReviewConflictException("相同練習 ID 的紀錄不一致。");
                return;
            }
            Execute(connection, transaction,
                "INSERT INTO practice_sessions(id,completed_ms,mode,topics_json,targets_json) VALUES($id,$at,$mode,$topics,$targets);",
                ("$id", Id(completion.Id)), ("$at", Ms(completion.CompletedAt)), ("$mode", (int)completion.Mode),
                ("$topics", JsonSerializer.Serialize(completion.Topics)), ("$targets", JsonSerializer.Serialize(completion.TargetIds)));
            InjectFault("Practice.BeforeCommit");
            transaction.Commit();
        }, token);

    public Task<IReadOnlyList<PracticeEligibility>> GetPracticeEligibilityAsync(Guid exerciseId,
        DateTimeOffset now, BigInteger dailyLimit, CancellationToken token = default) =>
        WithMaintenanceAsync<IReadOnlyList<PracticeEligibility>>(() =>
        {
            if (dailyLimit < 0) throw new ArgumentOutOfRangeException(nameof(dailyLimit));
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var exercise = ReadPractice(connection, transaction, exerciseId);
            if (exercise is null) return [];
            // Inspect quota without freezing today's adaptive limit until an actual rating/regular review.
            var day = LocalDay(now);
            var exists = Scalar(connection, transaction, "SELECT 1 FROM daily_limits WHERE local_day=$day;", ("$day", day)) is not null;
            BigInteger quota = dailyLimit;
            int? adaptiveLimit = null;
            if (exists)
            {
                var adaptive = Scalar(connection, transaction, "SELECT adaptive_limit FROM daily_limits WHERE local_day=$day;", ("$day", day));
                if (adaptive is not null and not DBNull) adaptiveLimit = Convert.ToInt32(adaptive);
            }
            else
            {
                var due = DueCount(connection, transaction, now);
                adaptiveLimit = ApplicationConfiguration.Current.Review.AdaptiveNewLimit(due);
            }
            if (adaptiveLimit is { } adaptiveQuota) quota = BigInteger.Min(quota, adaptiveQuota);
            var startedToday = StartedCount(connection, transaction, day);
            var hasSpace = startedToday < quota;
            var quotaMessage = $"未學新詞 · 今日新詞名額已用完（已開始 {startedToday} 個／今日上限 {quota} 個）。" +
                (adaptiveLimit is { } cap && cap < dailyLimit
                    ? "到期複習較多，今天的新詞名額已減少；可先標星，明天再評分。"
                    : "可先標星，明天再評分，或到設定調整每日新詞上限。");
            var result = new List<PracticeEligibility>();
            foreach (var sense in exercise.TargetIds)
            {
                var word = ReadWord(connection, transaction, sense);
                if (word is null) continue;
                var (schedule, version) = ReadSchedule(connection, transaction, sense);
                var operation = Scalar(connection, transaction, "SELECT operation_id FROM practice_ratings WHERE exercise_id=$e AND sense_id=$s;",
                    ("$e", Id(exerciseId)), ("$s", Id(sense))) as string;
                var started = Scalar(connection, transaction, "SELECT 1 FROM new_starts WHERE sense_id=$s;", ("$s", Id(sense))) is not null;
                var message = operation is not null ? $"已更新本次評分 · 下次複習：{schedule.DueAt.ToLocalTime():MM/dd HH:mm}" :
                    word.IsPaused || word.IsArchived || word.Enrollment == Enrollment.Skipped ? "此詞義已暫停或封存" :
                    schedule.LastReviewAt is null ? hasSpace || started ? $"未學新詞 · 評分後開始學習（今日已開始 {startedToday} 個／上限 {quota} 個）" : quotaMessage :
                    schedule.DueAt > now ? $"下次複習：{schedule.DueAt.ToLocalTime():MM/dd HH:mm}" : "已到期 · 可以評分";
                var canRate = operation is null && !word.IsPaused && !word.IsArchived && word.Enrollment != Enrollment.Skipped &&
                    (schedule.LastReviewAt is null ? hasSpace || started : schedule.DueAt <= now);
                var newLimitBlocked = operation is null && !word.IsPaused && !word.IsArchived && word.Enrollment != Enrollment.Skipped &&
                    schedule.LastReviewAt is null && !hasSpace && !started;
                result.Add(new(sense, version, canRate, message, word.IsStarred, operation is null ? null : Guid.Parse(operation), newLimitBlocked));
            }
            return result;
        }, token);

    public Task<ReviewResult> RatePracticeAsync(Guid exerciseId, ReviewSubmission submission,
        BigInteger dailyLimit, CancellationToken token = default)
    {
        if (exerciseId == Guid.Empty || dailyLimit < 0) throw new ArgumentException("練習評分無效。");
        return SubmitReviewInternalAsync(submission, exerciseId, dailyLimit, token);
    }

    private void PurgePractice(SqliteConnection connection, SqliteTransaction? transaction, DateTimeOffset now)
    {
        var day = TimeZoneInfo.ConvertTime(now, _timeZone).Date.AddMonths(-ApplicationConfiguration.Current.Practice.HistoryMonths);
        var cutoff = new DateTimeOffset(day, _timeZone.GetUtcOffset(day));
        Execute(connection, transaction, "DELETE FROM practice_sessions WHERE completed_ms<$cutoff;", ("$cutoff", Ms(cutoff)));
    }
    private static PracticeCompletion? ReadPractice(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = Command(connection, transaction, "SELECT id,completed_ms,mode,topics_json,targets_json FROM practice_sessions WHERE id=$id;", ("$id", Id(id)));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodePractice(reader) : null;
    }
    private static PracticeCompletion DecodePractice(SqliteDataReader reader)
    {
        var completion = new PracticeCompletion(Guid.Parse(reader.GetString(0)), FromMs(reader.GetInt64(1)), (PracticeMode)reader.GetInt32(2),
            JsonSerializer.Deserialize<string[]>(reader.GetString(3))!, JsonSerializer.Deserialize<Guid[]>(reader.GetString(4))!);
        ValidatePractice(completion);
        return completion;
    }
    private static void ValidatePractice(PracticeCompletion completion)
    {
        if (completion.Id == Guid.Empty || !Enum.IsDefined(completion.Mode) || completion.Topics is null ||
            completion.TargetIds is null || completion.Topics.Length is < 1 or > 30 ||
            completion.Topics.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80) ||
            completion.TargetIds.Length is < 1 or > 20 || completion.TargetIds.Any(x => x == Guid.Empty) ||
            completion.TargetIds.Distinct().Count() != completion.TargetIds.Length)
            throw new StudyDataException("練習紀錄無效。");
    }
}
