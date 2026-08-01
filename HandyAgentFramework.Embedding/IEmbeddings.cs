using System.Numerics.Tensors;

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
    Task<IEmbeddingResult<TElement>> Embed(string text, CancellationToken cancellation = default);

    /// <summary>
    /// Embed many items in one request
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task<IReadOnlyList<IEmbeddingResult<TElement>>> Embed(IReadOnlyList<string> text, CancellationToken cancellation = default);

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
    IEmbeddingResult<TElement> Create(string input, Memory<TElement> elements);
}

public interface IEmbeddingResult
{
    string Input { get; }
    string Model { get; }

    float Similarity(IEmbeddingResult other);
}

public interface IEmbeddingResult<TElement>
    : IEmbeddingResult
{
    public Memory<TElement> Result { get; }
}

/// <summary>
/// Result of an embedding operation
/// </summary>
/// <param name="Input">String that was embedded</param>
/// <param name="Model">Model name</param>
/// <param name="Result">The actual embedding</param>
public abstract record EmbeddingResult<TSelf, TElement>(string Input, string Model, Memory<TElement> Result)
    : IEmbeddingResult<TElement>
    where TSelf : EmbeddingResult<TSelf, TElement>
{
    /// <summary>
    /// Calculate the similarity between this embedding and another. Models must match.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public abstract float Similarity(TSelf other);

    /// <summary>
    /// Calculate the similarity between this embedding and another. Models and embedding types must match.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public float Similarity(IEmbeddingResult other)
    {
        if (other is not TSelf typedOther)
            throw new InvalidOperationException($"Cannot compare embeddings of type {GetType().Name} and {other.GetType().Name}");
        return Similarity(typedOther);
    }
}

/// <summary>
/// Result of an embedding operation that produce floating point values
/// </summary>
/// <param name="Input"></param>
/// <param name="Model"></param>
/// <param name="Result"></param>
public record FloatEmbeddingResult(string Input, string Model, Memory<float> Result)
    : EmbeddingResult<FloatEmbeddingResult, float>(Input, Model, Result)
{
    public override float Similarity(FloatEmbeddingResult other)
    {
        return TensorPrimitives.Dot(Result.Span, other.Result.Span);
    }
}