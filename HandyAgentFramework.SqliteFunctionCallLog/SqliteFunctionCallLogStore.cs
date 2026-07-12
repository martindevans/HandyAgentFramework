using Dapper;
using HandyAgentFramework.FunctionCall.Middleware;
using Microsoft.Extensions.AI;
using System.Data;
using System.Text.Json;

namespace HandyAgentFramework.SqliteFunctionCallLog;

/// <summary>
/// Provides a connection to an SQLite database for <see cref="SqliteFunctionCallLogStore"/>
/// </summary>
public interface ISqliteFunctionCallLogStoreConnectionProvider
{
    /// <summary>
    /// Get a database connection
    /// </summary>
    /// <returns></returns>
    IDbConnection GetConnection();
}

/// <summary>
/// Provides serializer options for <see cref="SqliteFunctionCallLogStore"/>
/// </summary>
/// <param name="Options"></param>
public record SqliteFunctionCallLogStoreSerializerOptions(JsonSerializerOptions Options);

/// <summary>
/// Logs function call results to an SQLite database
/// </summary>
public class SqliteFunctionCallLogStore
    : IFunctionCallLogStore
{
    private readonly ISqliteFunctionCallLogStoreConnectionProvider _database;
    private readonly JsonSerializerOptions _options;

    public SqliteFunctionCallLogStore(ISqliteFunctionCallLogStoreConnectionProvider database, SqliteFunctionCallLogStoreSerializerOptions? options = null)
    {
        _database = database;
        _options = options?.Options ?? new();
    }

    private static async Task Init(IDbConnection connection)
    {
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS SqliteFunctionCallLogStoreSuccessfulCalls
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ArgumentsJson TEXT NOT NULL,
                ResultJson TEXT,
                LoggedAt INTEGER NOT NULL
            );
            """
        );
        
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS SqliteFunctionCallLogStoreCallExceptions
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ArgumentsJson TEXT NOT NULL,
                Exception TEXT NOT NULL,
                LoggedAt INTEGER NOT NULL
            );
            """
        );
    }

    public async ValueTask LogCall(string name, AIFunctionArguments args, object? result)
    {
        using var connection = _database.GetConnection();
        await Init(connection);

        var argumentsJson = JsonSerializer.Serialize(args.ToDictionary(), _options);
        var resultJson = result != null ? JsonSerializer.Serialize(result, _options) : (string?)null;
        var loggedAt = ToUnixTimestamp(DateTime.UtcNow);

        await connection.ExecuteAsync(
            """
            INSERT INTO SqliteFunctionCallLogStoreSuccessfulCalls (Name, ArgumentsJson, ResultJson, LoggedAt)
            VALUES (@Name, @ArgumentsJson, @ResultJson, @LoggedAt);
            """,
            new
            {
                Name = name,
                ArgumentsJson = argumentsJson,
                ResultJson = resultJson,
                LoggedAt = loggedAt,
            }
        );
    }

    public async ValueTask LogException(string name, AIFunctionArguments args, Exception exception)
    {
        using var connection = _database.GetConnection();
        await Init(connection);

        var argumentsJson = JsonSerializer.Serialize(args.ToDictionary(), _options);
        var exceptionText = exception.ToString();
        var loggedAt = ToUnixTimestamp(DateTime.UtcNow);

        await connection.ExecuteAsync(
            """
            INSERT INTO SqliteFunctionCallLogStoreCallExceptions (Name, ArgumentsJson, Exception, LoggedAt)
            VALUES (@Name, @ArgumentsJson, @Exception, @LoggedAt);
            """,
            new
            {
                Name = name,
                ArgumentsJson = argumentsJson,
                Exception = exceptionText,
                LoggedAt = loggedAt,
            }
        );
    }

    public async ValueTask<IReadOnlyList<IFunctionCallLogStore.SuccessfulCall>> GetSuccessfulCalls(string? name = null, DateTime? after = null, DateTime? before = null)
    {
        // Connect to DB
        using var connection = _database.GetConnection();
        await Init(connection);

        // Get data
        var entries = (await connection.QueryAsync<DbSuccessfulCallEntry>(
            """
            SELECT *
            FROM SuccessfulCalls
            WHERE @Name IS NULL OR Name = @Name
            AND @After IS NULL OR LoggedAt >= @After
            AND @Before IS NULL OR LoggedAt <= @Before
            ORDER BY LoggedAt DESC;
            """,
            new
            {
                Name = name,
                After = after.HasValue ? (ulong?)ToUnixTimestamp(after.Value) : null,
                Before = before.HasValue ? (ulong?)ToUnixTimestamp(before.Value) : null,
            }
        )).ToList();

        // Convert to return
        return (
            from item in entries
            select new IFunctionCallLogStore.SuccessfulCall(item.Id, item.Name, item.ArgumentsJson, item.ResultJson, FromUnixTimestamp(item.LoggedAt))
        ).ToList();
    }

    public async ValueTask<IReadOnlyList<IFunctionCallLogStore.ExceptionalCall>> GetExceptionalCalls(string? name = null, DateTime? after = null, DateTime? before = null)
    {
        using var connection = _database.GetConnection();
        await Init(connection);

        var entries = (await connection.QueryAsync<DbCallExceptionEntry>(
            """
            SELECT *
            FROM CallExceptions
            WHERE @Name IS NULL OR Name = @Name
              AND @After IS NULL OR LoggedAt >= @After
              AND @Before IS NULL OR LoggedAt <= @Before
            ORDER BY LoggedAt DESC;
            """,
            new
            {
                Name = name,
                After = after.HasValue ? (ulong?)ToUnixTimestamp(after.Value) : null,
                Before = before.HasValue ? (ulong?)ToUnixTimestamp(before.Value) : null,
            }
        )).ToList();

        // Convert to return
        return (
            from item in entries
            select new IFunctionCallLogStore.ExceptionalCall(item.Id, item.Name, item.ArgumentsJson, item.Exception, FromUnixTimestamp(item.LoggedAt))
        ).ToList();
    }

    #region helpers
    private static long ToUnixTimestamp(DateTime datetime) => new DateTimeOffset(datetime).ToUnixTimeMilliseconds();

    private static DateTime FromUnixTimestamp(long timestamp) => DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
    #endregion

    #region models
    private record DbSuccessfulCallEntry(long Id, string Name, string ArgumentsJson, string? ResultJson, long LoggedAt);

    private record DbCallExceptionEntry(long Id, string Name, string ArgumentsJson, string Exception, long LoggedAt);
    #endregion
}
