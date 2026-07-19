using HandyAgentFramework.Embedding;
using HandyAgentFramework.Embedding.SqliteCache;

namespace HandyAgentFramework.Tests;

[TestClass]
public sealed class SqliteEmbeddingCacheTests
{
    const string INPUT_HELLO = "Hello";
    const string INPUT_WORLD = "world";

    [TestMethod]
    public async Task SingleEmbed_CacheMiss_GoesToProvider()
    {
        using var provider = new TestDatabaseProvider();

        var dummy = new DummyEmbeddingsProvider(dimensions: 4);
        var cache = new SqliteEmbeddingCache(dummy, provider);

        var result = await cache.Embed(INPUT_HELLO);

        Assert.AreEqual(INPUT_HELLO, result.Input);
        Assert.AreEqual(cache.Model, result.Model);

        Assert.AreEqual(1, dummy.CallCount);

        var expected = Embed(INPUT_HELLO, dummy.Dimensions);
        Assert.AreSequenceEqual(expected, result.Result.ToArray());
    }

    [TestMethod]
    public async Task SingleEmbed_CacheHit_ReturnsCached()
    {
        using var provider = new TestDatabaseProvider();

        var dummy = new DummyEmbeddingsProvider(dimensions: 4);
        var cache = new SqliteEmbeddingCache(dummy, provider);

        _ = await cache.Embed(INPUT_HELLO);
        var result = await cache.Embed(INPUT_HELLO);

        Assert.AreEqual(1, dummy.CallCount);

        var expected = Embed(INPUT_HELLO, dummy.Dimensions);
        Assert.AreSequenceEqual(expected, result.Result.ToArray());
    }

    [TestMethod]
    public async Task SingleEmbed_DifferentText_CallsProvider()
    {
        using var provider = new TestDatabaseProvider();

        var dummy = new DummyEmbeddingsProvider(dimensions: 4);
        var cache = new SqliteEmbeddingCache(dummy, provider);

        var result1 = await cache.Embed(INPUT_HELLO);
        var result2 = await cache.Embed(INPUT_WORLD);

        Assert.AreEqual(2, dummy.CallCount);

        var expected1 = Embed(INPUT_HELLO, dummy.Dimensions);
        Assert.AreSequenceEqual(expected1, result1.Result.ToArray());

        var expected2 = Embed(INPUT_WORLD, dummy.Dimensions);
        Assert.AreSequenceEqual(expected2, result2.Result.ToArray());
    }

    [TestMethod]
    public async Task BatchEmbed_AllMiss_GoesToProvider()
    {
        using var provider = new TestDatabaseProvider();

        var dummy = new DummyEmbeddingsProvider(dimensions: 4);
        var cache = new SqliteEmbeddingCache(dummy, provider);

        var texts = new[] { "hello", "world", "foo" };
        var results = await cache.Embed(texts);

        Assert.AreEqual(1, dummy.CallCount);
        Assert.HasCount(3, results);

        for (int i = 0; i < texts.Length; i++)
        {
            Assert.AreEqual(texts[i], results[i].Input);
            Assert.AreSequenceEqual(Embed(texts[i], dummy.Dimensions), results[i].Result.ToArray());
        }
    }

    [TestMethod]
    public async Task BatchEmbed_SomeHit_CallsProviderOnlyForMissing()
    {
        using var provider = new TestDatabaseProvider();

        var dummy = new DummyEmbeddingsProvider(dimensions: 4);
        var cache = new SqliteEmbeddingCache(dummy, provider);

        // Warm up the cache with "hello" and "world"
        _ = await cache.Embed("hello");
        _ = await cache.Embed("world");
        dummy.CallCount = 0;

        // Batch with mix: hello (cached), bar (new), world (cached)
        var texts = new[] { "hello", "bar", "world" };
        var results = await cache.Embed((IReadOnlyList<string>)texts);

        Assert.AreEqual(1, dummy.CallCount);
        Assert.HasCount(3, results);

        for (int i = 0; i < texts.Length; i++)
        {
            Assert.AreEqual(texts[i], results[i].Input);
            Assert.AreSequenceEqual(Embed(texts[i], dummy.Dimensions), results[i].Result.ToArray());
        }
    }

    [TestMethod]
    public async Task DifferentModel_CachesIndependently()
    {
        using var provider = new TestDatabaseProvider();

        var embeddingA = new DummyEmbeddingsProvider(dimensions: 2, model: "model-a");
        var embeddingB = new DummyEmbeddingsProvider(dimensions: 3, model: "model-b");

        var cacheA = new SqliteEmbeddingCache(embeddingA, provider);
        var cacheB = new SqliteEmbeddingCache(embeddingB, provider);

        // Cache embedding with model A
        var resultA = await cacheA.Embed(INPUT_HELLO);
        Assert.HasCount(2, resultA.Result);
        Assert.AreEqual(resultA.Model, embeddingA.Model);
        Assert.AreEqual(INPUT_HELLO, resultA.Input);
        
        // Model B should not have it cached (different model), returns different dimensions
        var resultB = await cacheB.Embed(INPUT_HELLO);
        Assert.HasCount(3, resultB.Result);
        Assert.AreEqual(1, embeddingB.CallCount);
        Assert.AreEqual(resultB.Model, embeddingB.Model);
    }

    private static float[] Embed(string text, int dimensions)
    {
        var result = new float[dimensions];
        var rng = new Random(text.GetHashCode());

        for (var i = 0; i < dimensions; i++)
            result[i] = rng.NextSingle();

        return result;
    }

    private sealed class DummyEmbeddingsProvider
        : IEmbeddings
    {
        public DummyEmbeddingsProvider(int dimensions, string? model = null)
        {
            Dimensions = dimensions;
            Model = model ?? $"dummy-{dimensions}d";
        }

        public int CallCount { get; set; }

        public string Model { get; }
        public int Dimensions { get; }

        public async Task<EmbeddingResult> Embed(string text, CancellationToken cancellation = default)
        {
            CallCount++;
            return new EmbeddingResult(text, Model, Embed(text));
        }

        public async Task<IReadOnlyList<EmbeddingResult>> Embed(IReadOnlyList<string> texts, CancellationToken cancellation = default)
        {
            CallCount++;
            var results = new List<EmbeddingResult>();
            foreach (var text in texts)
                results.Add(new EmbeddingResult(text, Model, Embed(text)));
            return results;
        }

        private float[] Embed(string text)
        {
            return SqliteEmbeddingCacheTests.Embed(text, Dimensions);
        }
    }
}
