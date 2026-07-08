using Dapper;
using HandyAgentFramework.Persistence;
using Microsoft.Agents.AI;
using System.Data;
using System.Text.Json;

namespace HandyAgentFramework.SqliteSessionStore
{
    /// <summary>
    /// Provides a connection to an SQLite database for <see cref="SqliteSessionStore"/>
    /// </summary>
    public interface ISqliteSessionStoreConnectionProvider
    {
        /// <summary>
        /// Get a database connection
        /// </summary>
        /// <returns></returns>
        IDbConnection GetConnection();
    }

    /// <summary>
    /// Provides serializer options for <see cref="SqliteSessionStore"/>
    /// </summary>
    /// <param name="Options"></param>
    public record SqliteSessionStoreSerializerOptions(JsonSerializerOptions Options);

    public class SqliteSessionStore
        : ISessionStore
    {
        private readonly ISqliteSessionStoreConnectionProvider _database;
        private readonly JsonSerializerOptions _options;

        public SqliteSessionStore(ISqliteSessionStoreConnectionProvider database, SqliteSessionStoreSerializerOptions? options = null)
        {
            _database = database;
            _options = options?.Options ?? new JsonSerializerOptions();
        }

        private static async Task Init(IDbConnection connection)
        {
            // Store directories with a pointer to parent directory
            await connection.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS KeyValueStore
                (
                    Context TEXT NOT NULL,
                    Key TEXT NOT NULL,
                    Json TEXT NOT NULL,
                
                    PRIMARY KEY (Context, Key)
                );
                """
            );
        }

        public async Task<AgentSession> Load(string context, string key, AIAgent agent, CancellationToken cancellation = default)
        {
            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Get JSON
            var json = await connection.QuerySingleOrDefaultAsync<string>(
                """
                SELECT Json
                FROM KeyValueStore
                WHERE Context = @Context
                  AND Key = @Key;
                """,
                new
                {
                    Context = context,
                    Key = key,
                }
            );

            // Create a new session
            if (json == null)
                return await agent.CreateSessionAsync(cancellation);

            // Deserialize
            return await agent.DeserializeSessionAsync(JsonElement.Parse(json), _options, cancellationToken:cancellation);
        }

        public async Task Save(string context, string key, AIAgent agent, AgentSession session, CancellationToken cancellation = default)
        {
            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // JSON serialise
            var jsonElement = await agent.SerializeSessionAsync(session, _options, cancellationToken: cancellation);
            var json = jsonElement.ToString();

            // Save
            await connection.ExecuteAsync(
                """
                INSERT INTO KeyValueStore (Context, Key, Json)
                VALUES (@Context, @Key, @Json)
                ON CONFLICT(Context, Key) DO UPDATE
                SET Json = excluded.Json;
                """,
                new
                {
                    Context = context,
                    Key = key,
                    Json = json,
                }
            );
        }
    }
}
