using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class SqliteStudyStore
{
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

    public Task SaveVocabularyBatchAsync(IReadOnlyList<VocabularyItem> items, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<Guid, bool>? starOverrides = null)
    {
        var snapshot = items.ToArray();
        var stars = starOverrides?.ToDictionary(x => x.Key, x => x.Value) ?? [];
        return WithMaintenanceAsync(() =>
        {
            if (snapshot.Length == 0 || snapshot.Select(x => x.Id).Distinct().Count() != snapshot.Length)
                throw new ArgumentException("請保存至少一個詞義，詞義 ID 不可重複。");
            if (stars.Keys.Except(snapshot.Select(x => x.Id)).Any())
                throw new ArgumentException("星號設定必須對應這次保存的詞義。");
            foreach (var item in snapshot) ValidateVocabulary(item);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var source in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Normalize(source);
                var explicitStar = stars.TryGetValue(item.Id, out var starred);
                if (explicitStar) item = item with { IsStarred = starred };
                var existing = ReadWord(connection, transaction, item.Id);
                if (existing is null)
                    InsertWord(connection, transaction, item with { IsUserEdited = true }, null);
                else
                {
                    WriteContent(connection, transaction, item, userEdited: true, preserveStar: !explicitStar);
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

    public Task SetStarredAsync(Guid senseId, bool starred, CancellationToken cancellationToken = default) =>
        WithMaintenanceAsync(() =>
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var item = ReadWord(connection, transaction, senseId) ?? throw new StudyDataException("找不到詞義。");
            Execute(connection, transaction, "UPDATE senses SET content_json=$json WHERE id=$id;",
                ("$json", JsonSerializer.Serialize(item with { IsStarred = starred }, JsonOptions)), ("$id", Id(senseId)));
            transaction.Commit();
        }, cancellationToken);

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

    private void InsertWord(SqliteConnection connection, SqliteTransaction transaction, VocabularyItem item, string? packId)
    {
        item = item with { CreatedAt = _clock.GetUtcNow() };
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
        VocabularyItem item, bool userEdited, bool preserveStar = true)
    {
        var existing = ReadWord(connection, transaction, item.Id);
        if (existing is not null) item = item with { IsStarred = preserveStar ? existing.IsStarred : item.IsStarred, CreatedAt = existing.CreatedAt };
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
            "SELECT s.id,s.content_json,s.enrollment,s.archived,s.paused,s.user_edited,c.stability,s.rowid FROM senses s LEFT JOIN cards c ON c.sense_id=s.id ORDER BY s.rowid;");
        using var reader = command.ExecuteReader();
        var result = new List<VocabularyItem>();
        while (reader.Read()) result.Add(DecodeWord(reader));
        return result;
    }

    private static VocabularyItem? ReadWord(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = Command(connection, transaction,
            "SELECT s.id,s.content_json,s.enrollment,s.archived,s.paused,s.user_edited,c.stability,s.rowid FROM senses s LEFT JOIN cards c ON c.sense_id=s.id WHERE s.id=$id;", ("$id", Id(id)));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeWord(reader) : null;
    }

    private static VocabularyItem DecodeWord(SqliteDataReader reader)
    {
        var item = JsonSerializer.Deserialize<VocabularyItem>(reader.GetString(1), JsonOptions)
            ?? throw new StudyDataException("無法讀取詞義內容。");
        return item with { Id = Guid.Parse(reader.GetString(0)), Enrollment = (Enrollment)reader.GetInt32(2),
            IsArchived = reader.GetBoolean(3), IsPaused = reader.GetBoolean(4), IsUserEdited = reader.GetBoolean(5),
            Stability = reader.IsDBNull(6) ? 0 : reader.GetDouble(6), CreationOrder = reader.GetInt64(7) };
    }

    private static VocabularyItem Normalize(VocabularyItem item) => item with
    {
        Headword = item.Headword.Trim(), Meaning = item.Meaning.Trim(), PartOfSpeech = item.PartOfSpeech.Trim(),
        Categories = item.Categories.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Collocations = item.Collocations.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(),
        Synonyms = item.Synonyms.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Notes = item.Notes.Trim(), EnglishDefinition = item.EnglishDefinition.Trim()
    };

    private static void ValidateVocabulary(VocabularyItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Headword) || item.Headword.Length > 200 ||
            string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000 ||
            item.EnglishDefinition is null || item.EnglishDefinition.Length > 4000 ||
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
}
