using System.Data;
using System.Runtime.InteropServices;
using Dapper;

namespace HandyAgentFramework.Embedding.SqliteCache;

/// <summary>
/// Provides a connection to an SQLite database for <see cref="SqliteEmbeddingCache{TElement}"/>
/// </summary>
public interface ISqliteEmbeddingCacheConnectionProvider
{
    /// <summary>
    /// Get a database connection
    /// </summary>
    /// <returns></returns>
    IDbConnection GetConnection();
}

public class SqliteEmbeddingCache<TElement>
    : IEmbeddings<TElement>
    where TElement : struct
{
    private readonly ISqliteEmbeddingCacheConnectionProvider _database;

    private readonly IEmbeddings<TElement> _embeddings;
    public string Model => _embeddings.Model;
    public int Dimensions => _embeddings.Dimensions;

    public SqliteEmbeddingCache(IEmbeddings<TElement> embeddings, ISqliteEmbeddingCacheConnectionProvider database)
    {
        _embeddings = embeddings;
        _database = database;
    }

    private static async Task Init(IDbConnection connection)
    {
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS `HandyAgentFramework_CachedEmbeddings` (
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

    public async Task<EmbeddingResult<TElement>> Embed(string text, CancellationToken cancellation = default)
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

    public async Task<IReadOnlyList<EmbeddingResult<TElement>>> Embed(IReadOnlyList<string> text, CancellationToken cancellation = default)
    {
        // Get DB
        using var connection = _database.GetConnection();
        await Init(connection);
            
        // Create output array and batch of work to do
        var results = new EmbeddingResult<TElement>[text.Count];
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
            foreach (var embeddingResult in batchEmbeddings)
                await StoreCachedEmbedding(connection, embeddingResult, now, null);
        }

        // Return final results
        return results;
    }

    private async Task<EmbeddingResult<TElement>?> FetchCachedEmbedding(string text, IDbConnection connection, ulong now)
    {
        var cached = await connection.QuerySingleOrDefaultAsync<CachedEmbedding>(
            """
            UPDATE HandyAgentFramework_CachedEmbeddings
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

        return _embeddings.Create(
            text,
            MemoryMarshal.Cast<byte, TElement>(cached.EmbeddingRaw.AsSpan()).ToArray()
        );
    }

    private static async Task StoreCachedEmbedding(IDbConnection connection, EmbeddingResult<TElement> embedding, ulong now, IDbTransaction? tsx)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO HandyAgentFramework_CachedEmbeddings (
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
                EmbeddingRaw = MemoryMarshal.Cast<TElement, byte>(embedding.Result.Span).ToArray(),
                LastAccessTime = now,
            },
            transaction: tsx
        );
    }

    public EmbeddingResult<TElement> Create(string input, Memory<TElement> elements)
    {
        return _embeddings.Create(input, elements);
    }

    private record CachedEmbedding(string Value, string Model, long Dimensions, byte[] EmbeddingRaw, long LastAccessTime);
}