using System.Text.Json;
using Microsoft.Data.Sqlite;
using Wording.Core;
using Wording.Infrastructure;

namespace Wording.Tests;

public sealed class VocabularyNotesTests
{
    private const string Metadata = VocabularyNotes.PackDisclaimer + "詞義參考：https://www.merriam-webster.com/dictionary/fill%20in";

    [Fact]
    public void RemovesPackBoilerplateButKeepsGrammarAndPersonalReferenceNotes()
    {
        Assert.Equal("可分：fill it in。", VocabularyNotes.Clean("可分：fill it in。 " + Metadata));
        Assert.Equal("", VocabularyNotes.Clean(Metadata));
        Assert.Equal("我的筆記。\n\n不要忘記過去式。", VocabularyNotes.Clean("我的筆記。\n" + Metadata + "\n不要忘記過去式。"));
        const string personal = "TOEIC 會遇到表格。詞義參考：https://www.merriam-webster.com/dictionary/fill%20in";
        Assert.Equal(personal, VocabularyNotes.Clean(personal));
        Assert.Equal("可分。", VocabularyNotes.Clean("可分。 " + VocabularyNotes.PackDisclaimer + "詞義參考：https://dictionary.cambridge.org/us/dictionary/english-polish/bring-forward"));
    }

    [Fact]
    public async Task ExistingNotesAreBackedUpAndCleanedWithoutChangingCardsStarsOrLearning()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        var word = StudyTestData.Word("fill in", "代班") with
        {
            IsStarred = true, Notes = "我的主解釋筆記。",
            AdditionalSenses = [new() { PartOfSpeech = "動詞片語", Meaning = "填表", Notes = "可分：fill it in。" }]
        };
        await data.Store.SaveVocabularyAsync(word);
        await data.Store.SubmitReviewAsync(new(word.Id, ReviewRating.Good, data.Clock.Now, Guid.NewGuid(), 0));
        await data.Store.SetPausedAsync(word.Id, true);
        var original = (await data.Store.GetVocabularyAsync()).Single();
        var old = original with { Notes = original.Notes + " " + Metadata,
            AdditionalSenses = [original.AdditionalSenses[0] with { Notes = original.AdditionalSenses[0].Notes + " " + Metadata }] };
        WriteLegacyContent(data, old);
        var schedule = ScheduleSnapshot(data);
        Assert.Equal(1, await data.Store.RemoveVocabularyNoteMetadataAsync());
        var cleaned = (await data.Store.GetVocabularyAsync()).Single();
        Assert.Equal(original.Notes, cleaned.Notes);
        Assert.Equal(original.AdditionalSenses, cleaned.AdditionalSenses);
        Assert.True(cleaned.IsStarred);
        Assert.True(cleaned.IsPaused);
        Assert.Equal(original.Enrollment, cleaned.Enrollment);
        Assert.Equal(schedule, ScheduleSnapshot(data));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM review_log;"));
        Assert.Equal(1L, data.Scalar("SELECT COUNT(*) FROM new_starts;"));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(data.DirectoryPath, "backups"), "before-note-cleanup-*.wtbackup"));
        Assert.Equal(0, await data.Store.RemoveVocabularyNoteMetadataAsync());
        Assert.Single(Directory.GetFiles(Path.Combine(data.DirectoryPath, "backups"), "before-note-cleanup-*.wtbackup"));

        using var restored = new StudyTestData();
        await restored.Store.InitializeAsync();
        await new BackupService(restored.Store).RestoreBackupAsync(backup);
        Assert.Equal(old.Notes, (await restored.Store.GetVocabularyAsync()).Single().Notes);

        // Reimporting an old pack must not put the boilerplate back or reset existing learning.
        var path = Path.Combine(data.DirectoryPath, "old-pack.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new VocabularyDocument(1, [VocabularyEntry.From(old)]), VocabularyFileService.JsonOptions));
        await new VocabularyFileService(data.Store).ImportAsync(path);
        Assert.Equal(original.Notes, (await data.Store.GetVocabularyAsync()).Single().Notes);
        Assert.Equal(original.AdditionalSenses, (await data.Store.GetVocabularyAsync()).Single().AdditionalSenses);
        Assert.Equal(schedule, ScheduleSnapshot(data));
    }

    [Fact]
    public async Task CleanupFailureRollsBackAllNotesAndKeepsLearning()
    {
        using var data = new StudyTestData();
        await data.Store.InitializeAsync();
        foreach (var headword in new[] { "fill in", "check out" })
        {
            var word = StudyTestData.Word(headword);
            await data.Store.SaveVocabularyAsync(word);
            WriteLegacyContent(data, word with { Notes = "保留用法。 " + Metadata });
        }
        data.FailAt = "VocabularyNotes.AfterItem";
        await Assert.ThrowsAsync<IOException>(() => data.Store.RemoveVocabularyNoteMetadataAsync());
        Assert.All(await data.Store.GetVocabularyAsync(), x => Assert.Contains(Metadata, x.Notes));
        Assert.Equal(2L, data.Scalar("SELECT COUNT(*) FROM cards;"));
        data.FailAt = null;
        Assert.Equal(2, await data.Store.RemoveVocabularyNoteMetadataAsync());
        Assert.All(await data.Store.GetVocabularyAsync(), x => Assert.Equal("保留用法。", x.Notes));
    }

    private static object? ScheduleSnapshot(StudyTestData data) => data.Scalar("""
        SELECT json_object('id',card_id,'state',state,'step',step,'due',due_ms,'version',version,
            'stability',stability,'difficulty',difficulty,'last',last_review_ms) FROM cards;
        """);

    private static void WriteLegacyContent(StudyTestData data, VocabularyItem word)
    {
        using var connection = new SqliteConnection($"Data Source={data.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE senses SET content_json=$json WHERE id=$id;";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(word, VocabularyFileService.JsonOptions));
        command.Parameters.AddWithValue("$id", word.Id.ToString("D"));
        command.ExecuteNonQuery();
    }
}
