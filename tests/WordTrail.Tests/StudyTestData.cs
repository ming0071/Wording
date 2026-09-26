using Microsoft.Data.Sqlite;
using WordTrail.Core;
using WordTrail.Infrastructure;

namespace WordTrail.Tests;

internal sealed class StudyTestData : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "WordTrailTests", Guid.NewGuid().ToString("N"));
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
