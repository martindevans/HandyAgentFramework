using System.Numerics.Tensors;

namespace HandyAgentFramework.Embedding.Adapters;

/// <summary>
/// Converts float embeddings to bit embeddings, 8x smaller and even faster to compare for similarity. Retains around 96% accuracy.
/// </summary>
public class BitwiseEmbeddingAdapter
    : IEmbeddings<byte>
{
    private readonly IEmbeddings<float> _inner;

    public string Model { get; }
    public int Dimensions { get; }

    public BitwiseEmbeddingAdapter(IEmbeddings<float> inner)
    {
        _inner = inner;
        
        Model = $"BitwiseEmbeddingAdapter({_inner.Model})";
        Dimensions = _inner.Dimensions;
    }

    public async Task<IEmbeddingResult<byte>> Embed(string text, CancellationToken cancellation = default)
    {
        var inner = await _inner.Embed(text, cancellation);

        return new ByteEmbedding(
            inner.Input,
            Model,
            FloatsToBytes(inner.Result)
        );
    }

    public async Task<IReadOnlyList<IEmbeddingResult<byte>>> Embed(IReadOnlyList<string> text, CancellationToken cancellation = default)
    {
        var inner = await _inner.Embed(text, cancellation);
        var result = new List<IEmbeddingResult<byte>>(inner.Count);

        foreach (var item in inner)
        {
            result.Add(new ByteEmbedding(
                item.Input,
                Model,
                FloatsToBytes(item.Result)
            ));
        }

        return result;
    }

    private static byte[] FloatsToBytes(ReadOnlyMemory<float> floatsMem)
    {
        var floats = floatsMem.Span;
        
        var bytes = new byte[(floats.Length + 7) / 8];
        for (var byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
        {
            var bits = 0;
            var baseIndex = byteIndex * 8;

            for (var bit = 0; bit < 8; bit++)
            {
                var floatIndex = baseIndex + bit;
                if (floatIndex >= floats.Length)
                    break;

                if (floats[floatIndex] > 0f)
                    bits |= 1 << bit;
            }

            bytes[byteIndex] = (byte)bits;
        }

        return bytes;
    }

    public IEmbeddingResult<byte> Create(string input, Memory<byte> elements)
    {
        return new ByteEmbedding(input, Model, elements);
    }

    public record ByteEmbedding(string Input, string Model, Memory<byte> Result)
        : EmbeddingResult<ByteEmbedding, byte>(Input, Model, Result)
    {
        public override float Similarity(ByteEmbedding other)
        {
            if (Model != other.Model)
                throw new ArgumentException($"Cannot compare embeddings with different models. {Model} != {other.Model}", nameof(other));
            if (Result.Length != other.Result.Length)
                throw new ArgumentException($"Cannot compare embeddings with different dimensions. {Result.Length} != {other.Result.Length}", nameof(other));

            var a = Result.Span;
            var b = other.Result.Span;
            var dimensions = a.Length * 8;
            
            var hamming = TensorPrimitives.HammingBitDistance(a, b);
            var similarity = 1f - (2f * hamming / dimensions);
            return similarity;
        }
    }
}