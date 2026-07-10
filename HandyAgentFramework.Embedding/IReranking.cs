namespace HandyAgentFramework.Embedding;

/// <summary>
/// Rerank documents according to a query
/// </summary>
public interface IReranking
{
    /// <summary>
    /// Rank a set of documents for relevance to the given query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="documents"></param>
    /// <param name="cancellation"></param>
    /// <returns>Rerank results, in order of relevance (most relevant first)</returns>
    Task<List<RerankResult>> Rerank(string query, IReadOnlyList<string> documents, CancellationToken cancellation = default);
}

/// <summary>
/// Result from reranking a set of documents
/// </summary>
/// <param name="Index">The index of the document in the input list</param>
/// <param name="Document">The document</param>
/// <param name="Relevance">The relevance of the document according to the re-ranking</param>
public readonly record struct RerankResult(string Document, int Index, float Relevance)
    : IComparable<RerankResult>
{
    /// <inheritdoc />
    public int CompareTo(RerankResult other)
    {
        return Relevance.CompareTo(other.Relevance);
    }
}