namespace HandyAgentFramework.Embedding;

/// <summary>
/// Provides functions to embed text
/// </summary>
public interface IEmbeddings<TElement>
{
    /// <summary>
    /// Embed a single item
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task<EmbeddingResult<TElement>> Embed(string text, CancellationToken cancellation = default);

    /// <summary>
    /// Embed many items in one request
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task<IReadOnlyList<EmbeddingResult<TElement>>> Embed(IReadOnlyList<string> text, CancellationToken cancellation = default);

    /// <summary>
    /// The name of model used for embeddings generation
    /// </summary>
    string Model { get; }

    /// <summary>
    /// The dimensionality of embeddings
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Create an embedding result from precalculated data
    /// </summary>
    /// <param name="input"></param>
    /// <param name="elements"></param>
    /// <returns></returns>
    EmbeddingResult<TElement> Create(string input, Memory<TElement> elements);
}

/// <summary>
/// Result of an embedding operation
/// </summary>
/// <param name="Input">String that was embedded</param>
/// <param name="Model">Model name</param>
/// <param name="Result">The actual embedding</param>
public abstract record EmbeddingResult<TElement>(string Input, string Model, Memory<TElement> Result)
{
    /// <summary>
    /// Calculate the similarity between this embedding and another. Models must match.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public abstract float Similarity(EmbeddingResult<TElement> other);
}