using AiCodeGraph.Core.Embeddings;

namespace AiCodeGraph.Tests;

public class HashEmbeddingEngineTests
{
    private readonly HashEmbeddingEngine _engine = new(384);

    [Fact]
    public void GenerateEmbedding_Returns384Dimensions()
    {
        var vector = _engine.GenerateEmbedding("test input text");
        Assert.Equal(384, vector.Length);
    }

    [Fact]
    public void GenerateEmbedding_IsDeterministic()
    {
        var v1 = _engine.GenerateEmbedding("process customer order");
        var v2 = _engine.GenerateEmbedding("process customer order");
        Assert.Equal(v1, v2);
    }

    [Fact]
    public void GenerateEmbedding_SimilarTextsHighSimilarity()
    {
        var v1 = _engine.GenerateEmbedding("get customer by id");
        var v2 = _engine.GenerateEmbedding("find customer by id");
        var similarity = CosineSimilarity(v1, v2);
        Assert.True(similarity > 0.5, $"Expected > 0.5, got {similarity}");
    }

    [Fact]
    public void GenerateEmbedding_DifferentTextsLowerSimilarity()
    {
        var v1 = _engine.GenerateEmbedding("get customer by id");
        var v2 = _engine.GenerateEmbedding("delete database connection");
        var similarity = CosineSimilarity(v1, v2);
        Assert.True(similarity < 0.5, $"Expected < 0.5, got {similarity}");
    }

    [Fact]
    public void GenerateEmbedding_IsNormalized()
    {
        var vector = _engine.GenerateEmbedding("test text");
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        Assert.InRange(magnitude, 0.99f, 1.01f);
    }

    [Fact]
    public void GenerateEmbedding_EmptyText_ReturnsZeroVector()
    {
        var vector = _engine.GenerateEmbedding("");
        Assert.All(vector, v => Assert.Equal(0f, v));
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0;
        for (int i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot; // Vectors are already normalized
    }
}

public class VectorIndexTests
{
    [Fact]
    public void Search_ReturnsTopK()
    {
        var index = new VectorIndex();
        var items = new List<(string Id, float[] Vector)>
        {
            ("a", CreateVector(1, 0, 0)),
            ("b", CreateVector(0, 1, 0)),
            ("c", CreateVector(0, 0, 1)),
            ("d", CreateVector(0.9f, 0.1f, 0)),
        };
        index.BuildIndex(items);

        var results = index.Search(CreateVector(1, 0, 0), 2);
        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Id);
        Assert.Equal("d", results[1].Id);
    }

    [Fact]
    public void Search_IdenticalVector_ScoreOne()
    {
        var index = new VectorIndex();
        index.BuildIndex(new List<(string, float[])>
        {
            ("a", CreateVector(1, 0, 0))
        });

        var results = index.Search(CreateVector(1, 0, 0), 1);
        Assert.Single(results);
        Assert.InRange(results[0].Score, 0.99f, 1.01f);
    }

    [Fact]
    public void Search_OrthogonalVectors_ScoreZero()
    {
        var index = new VectorIndex();
        index.BuildIndex(new List<(string, float[])>
        {
            ("a", CreateVector(1, 0, 0))
        });

        var results = index.Search(CreateVector(0, 1, 0), 1);
        Assert.Single(results);
        Assert.InRange(results[0].Score, -0.01f, 0.01f);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new VectorIndex();
        index.BuildIndex(new List<(string, float[])>());
        Assert.Empty(index.Search(CreateVector(1, 0, 0)));
    }

    [Fact]
    public void AddItem_IncreasesCount()
    {
        var index = new VectorIndex();
        Assert.Equal(0, index.Count);
        index.AddItem("a", CreateVector(1, 0, 0));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void SaveAndLoad_PreservesSearchResults()
    {
        var index = new VectorIndex();
        index.BuildIndex(new List<(string, float[])>
        {
            ("method1", CreateVector(1, 0, 0)),
            ("method2", CreateVector(0, 1, 0)),
            ("method3", CreateVector(0.7f, 0.7f, 0))
        });

        var tempFile = Path.GetTempFileName();
        try
        {
            index.SaveToDisk(tempFile);

            var loaded = new VectorIndex();
            loaded.LoadFromDisk(tempFile);

            Assert.Equal(3, loaded.Count);
            var results = loaded.Search(CreateVector(1, 0, 0), 2);
            Assert.Equal("method1", results[0].Id);
            Assert.Equal("method3", results[1].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromDisk_InvalidFormat_Throws()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0, 0, 0, 0 });
            var index = new VectorIndex();
            Assert.Throws<InvalidDataException>(() => index.LoadFromDisk(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WorksWith384Dimensions()
    {
        var engine = new HashEmbeddingEngine(384);
        var index = new VectorIndex();

        var items = new List<(string, float[])>
        {
            ("m1", engine.GenerateEmbedding("get customer by id")),
            ("m2", engine.GenerateEmbedding("find customer by identifier")),
            ("m3", engine.GenerateEmbedding("delete all logs"))
        };
        index.BuildIndex(items);

        var query = engine.GenerateEmbedding("get customer by id");
        var results = index.Search(query, 3);

        Assert.Equal("m1", results[0].Id); // exact match first
        Assert.True(results[0].Score > results[2].Score); // similar > dissimilar
    }

    [Fact]
    public void AddItem_NullVector_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        Assert.Throws<ArgumentNullException>(() => index.AddItem("id", null!));
    }

    [Fact]
    public void AddItem_NaNVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        var ex = Assert.Throws<ArgumentException>(() => index.AddItem("id", new float[] { 1.0f, float.NaN, 0.5f }));
        Assert.Contains("NaN", ex.Message);
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void AddItem_InfinityVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        var ex = Assert.Throws<ArgumentException>(() => index.AddItem("id", new float[] { float.PositiveInfinity, 0.5f }));
        Assert.Contains("Infinity", ex.Message);
    }

    [Fact]
    public void AddItem_NegativeInfinityVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        Assert.Throws<ArgumentException>(() => index.AddItem("id", new float[] { 0.5f, float.NegativeInfinity }));
    }

    [Fact]
    public void BuildIndex_NullVector_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        var items = new List<(string, float[])> { ("id", null!) };
        Assert.Throws<ArgumentNullException>(() => index.BuildIndex(items));
    }

    [Fact]
    public void BuildIndex_NaNVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        var items = new List<(string, float[])> { ("id", new float[] { 1.0f, float.NaN }) };
        Assert.Throws<ArgumentException>(() => index.BuildIndex(items));
    }

    [Fact]
    public void Search_NullQuery_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        index.AddItem("id", new float[] { 1.0f, 0.5f, 0.3f });
        Assert.Throws<ArgumentNullException>(() => index.Search(null!));
    }

    [Fact]
    public void Search_NaNQuery_ThrowsArgument()
    {
        var index = new VectorIndex();
        index.AddItem("id", new float[] { 1.0f, 0.5f, 0.3f });
        Assert.Throws<ArgumentException>(() => index.Search(new float[] { float.NaN, 0.5f, 0.3f }));
    }

    private static float[] CreateVector(params float[] values) => values;
}
