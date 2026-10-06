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
            var result = new List<PracticeEligibility>();
            foreach (var sense in exercise.TargetIds)
            {
                var word = ReadWord(connection, transaction, sense);
                if (word is null) continue;
                var (schedule, version) = ReadSchedule(connection, transaction, sense);
                var operation = Scalar(connection, transaction, "SELECT operation_id FROM practice_ratings WHERE exercise_id=$e AND sense_id=$s;",
                    ("$e", Id(exerciseId)), ("$s", Id(sense))) as string;
                var message = operation is not null ? $"已更新本次評分 · 下次複習：{schedule.DueAt.ToLocalTime():MM/dd HH:mm}" :
                    word.IsPaused || word.IsArchived || word.Enrollment == Enrollment.Skipped ? "此詞義已暫停或封存" :
                    schedule.LastReviewAt is null ? "未學新詞 · 評分後開始學習，計入今日新詞數量，不受每日上限限制" :
                    schedule.DueAt > now ? $"下次複習：{schedule.DueAt.ToLocalTime():MM/dd HH:mm}" : "已到期 · 可以評分";
                var canRate = operation is null && !word.IsPaused && !word.IsArchived && word.Enrollment != Enrollment.Skipped &&
                    (schedule.LastReviewAt is null || schedule.DueAt <= now);
                result.Add(new(sense, version, canRate, message, word.IsStarred, operation is null ? null : Guid.Parse(operation)));
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
