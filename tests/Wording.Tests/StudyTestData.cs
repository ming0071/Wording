using Microsoft.Data.Sqlite;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Tests;

internal sealed class StudyTestData : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "WordingTests", Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(DirectoryPath, "study.sqlite");
    public TestClock Clock { get; } = new();
    public string? FailAt { get; set; }
    public SqliteStudyStore Store { get; }

    public StudyTestData()
    {
        Directory.CreateDirectory(DirectoryPath);
        Store = NewStore();
    }

    public SqliteStudyStore NewStore() => new(DatabasePath, Clock, TimeZoneInfo.Utc,
        point => { if (point == FailAt) throw new IOException("Injected failure: " + point); });

    public static VocabularyItem Word(string headword = "appointment", string meaning = "預約") => new()
    {
        Headword = headword, Meaning = meaning, PartOfSpeech = "noun", Cue = "a meeting arranged in advance",
        Categories = ["商業", "旅行"], Collocations = ["make an appointment"],
        Examples = [new("I have an appointment at nine.", "我九點有預約。")],
        Enrollment = Enrollment.Selected
    };

    public async Task<VocabularyItem> AddWordAsync(string headword = "appointment")
    {
        var item = Word(headword);
        await Store.SaveVocabularyAsync(item);
        return item;
    }

    public object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False;Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False;Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void UseLegacyLimitsSchema() => Execute("""
        ALTER TABLE daily_limits RENAME TO daily_limits_v2;
        CREATE TABLE daily_limits(local_day TEXT NOT NULL PRIMARY KEY,
          adaptive_limit INTEGER NOT NULL CHECK(adaptive_limit BETWEEN 0 AND 10),
          configured_limit INTEGER NOT NULL CHECK(configured_limit BETWEEN 0 AND 10));
        INSERT INTO daily_limits SELECT local_day,COALESCE(adaptive_limit,5),CAST(configured_limit AS INTEGER) FROM daily_limits_v2;
        DROP TABLE daily_limits_v2;
        PRAGMA user_version=1;
        """);

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
