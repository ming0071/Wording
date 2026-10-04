using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Wording.Core;

namespace Wording.Infrastructure;

/// <summary>
/// 單人本機詞库：短交易、明確 SQL、一個維護鎖。
/// SQLite 的 async API 仍是同步 I/O，所以整個資料操作在背景執行緒完成。
/// </summary>
public sealed partial class SqliteStudyStore : IStudyStore, IPracticeStore
{
    internal const int SchemaVersion = 4;
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
