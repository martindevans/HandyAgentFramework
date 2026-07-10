using System.Data;
using System.Runtime.InteropServices;
using Dapper;

namespace HandyAgentFramework.Embedding.SqliteCache
{
    /// <summary>
    /// Provides a connection to an SQLite database for <see cref="SqliteEmbeddingCache"/>
    /// </summary>
    public interface ISqliteEmbeddingCacheConnectionProvider
    {
        /// <summary>
        /// Get a database connection
        /// </summary>
        /// <returns></returns>
        IDbConnection GetConnection();
    }

    public class SqliteEmbeddingCache
        : IEmbeddings
    {
        private readonly ISqliteEmbeddingCacheConnectionProvider _database;

        private readonly IEmbeddings _embeddings;
        public string Model => _embeddings.Model;
        public int Dimensions => _embeddings.Dimensions;

        public SqliteEmbeddingCache(IEmbeddings embeddings, ISqliteEmbeddingCacheConnectionProvider database)
        {
            _embeddings = embeddings;
            _database = database;
        }

        private static async Task Init(IDbConnection connection)
        {
            await connection.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS `CachedEmbeddings` (
                    `Value` TEXT NOT NULL,
                    `Model` TEXT NOT NULL,
                    `Dimensions` INTEGER NOT NULL,
                    `EmbeddingRaw` BLOB NOT NULL,
                    `LastAccessTime` INTEGER NOT NULL,
                    UNIQUE (Value, Model, Dimensions)
                );
                """
            );
        }


        public async Task<EmbeddingResult> Embed(string text, CancellationToken cancellation = default)
        {
            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Get the cached embedding if it exists
            var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var cached = await FetchCachedEmbedding(text, connection, now);

            // Return the cached result
            if (cached != null)
                return cached;
            
            // Do the actual embedding
            var embedding = await _embeddings.Embed(text, cancellation);

            // Insert into cache
            await StoreCachedEmbedding(connection, embedding, now, null);
            
            // Return final result
            return embedding;
        }

        public async Task<IReadOnlyList<EmbeddingResult>> Embed(IReadOnlyList<string> text, CancellationToken cancellation = default)
        {
            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);
            
            // Create output array and batch of work to do
            var results = new EmbeddingResult[text.Count];
            var batch = new List<string>();
            var batchIndices = new List<int>();
            
            // Fetch as many cached results as possible
            var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (var i = 0; i < text.Count; i++)
            {
                var cached = await FetchCachedEmbedding(text[i], connection, now);

                if (cached != null)
                {
                    results[i] = cached;
                }
                else
                {
                    batch.Add(text[i]);
                    batchIndices.Add(i);
                }
            }

            if (batch.Count > 0)
            {
                // Embed the batch of work
                var batchEmbeddings = await _embeddings.Embed(batch, cancellation);

                // Distribute embeddings results
                for (var i = 0; i < batchEmbeddings.Count; i++)
                    results[batchIndices[i]] = batchEmbeddings[i];

                // Store results in cache
                using (var tsx = connection.BeginTransaction())
                {
                    foreach (var embeddingResult in batchEmbeddings)
                        await StoreCachedEmbedding(connection, embeddingResult, now, tsx);
                    tsx.Commit();
                }
            }

            // Return final results
            return results;
        }

        private async Task<EmbeddingResult?> FetchCachedEmbedding(string text, IDbConnection connection, ulong now)
        {
            var cached = await connection.QuerySingleOrDefaultAsync<CachedEmbedding>(
                """
                UPDATE CachedEmbeddings
                SET LastAccessTime = @Now
                WHERE Value = @Value
                  AND Model = @Model
                  AND Dimensions = @Dimensions
                RETURNING
                    Value,
                    Model,
                    Dimensions,
                    EmbeddingRaw,
                    LastAccessTime;
                """,
                new
                {
                    Now = now,
                    Value = text,
                    Model = Model,
                    Dimensions = Dimensions,
                });

            if (cached == null)
                return null;

            return new EmbeddingResult(
                text,
                Model,
                MemoryMarshal.Cast<byte, float>(cached.EmbeddingRaw.AsSpan()).ToArray()
            );
        }

        private static async Task StoreCachedEmbedding(IDbConnection connection, EmbeddingResult embedding, ulong now, IDbTransaction? tsx)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO CachedEmbeddings (
                    Value,
                    Model,
                    Dimensions,
                    EmbeddingRaw,
                    LastAccessTime
                )
                VALUES (
                    @Value,
                    @Model,
                    @Dimensions,
                    @EmbeddingRaw,
                    @LastAccessTime
                )
                ON CONFLICT (Value, Model, Dimensions) DO NOTHING;
                """,
                new
                {
                    Value = embedding.Input,
                    Model = embedding.Model,
                    Dimensions = embedding.Result.Length,
                    EmbeddingRaw = MemoryMarshal.Cast<float, byte>(embedding.Result.Span).ToArray(),
                    LastAccessTime = now,
                },
                transaction: tsx
            );
        }

        private record CachedEmbedding(string Value, string Model, int Dimensions, byte[] EmbeddingRaw, ulong LastAccessTime);
    }
}
