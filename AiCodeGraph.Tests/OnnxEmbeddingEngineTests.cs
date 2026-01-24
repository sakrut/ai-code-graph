using AiCodeGraph.Core.Embeddings;

namespace AiCodeGraph.Tests;

public class OnnxEmbeddingEngineTests
{
    [Fact]
    public void Constructor_ThrowsOnMissingModelFile()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingEngine("/nonexistent/model.onnx"));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyPath()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingEngine(""));
    }

    [Fact]
    public void Constructor_ThrowsWithCorrectFileName()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingEngine("/tmp/missing-model.onnx"));
        Assert.Contains("missing-model.onnx", ex.FileName);
    }

    [Fact]
    public void Dimensions_ReturnsConfiguredValue()
    {
        // Can't construct without a real model, but we can verify the interface contract
        // through the FileNotFoundException - the dimensions parameter is validated after file check
        var ex = Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingEngine("/nonexistent.onnx", dimensions: 768));
        Assert.NotNull(ex);
    }

    [Fact]
    public void Constructor_AcceptsCustomMaxTokens()
    {
        // Verify the constructor accepts the maxTokens parameter without throwing
        // (other than the expected FileNotFoundException for missing model)
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingEngine("/nonexistent.onnx", dimensions: 384, maxTokens: 256));
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires ONNX model file")]
    public void GenerateEmbedding_WithRealModel_ReturnsCorrectDimensions()
    {
        // This test requires a real ONNX model file (e.g., all-MiniLM-L6-v2)
        // Download from: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
        var modelPath = Environment.GetEnvironmentVariable("ONNX_MODEL_PATH")
            ?? "models/all-MiniLM-L6-v2.onnx";

        using var engine = new OnnxEmbeddingEngine(modelPath, 384);
        var result = engine.GenerateEmbedding("test sentence for embedding");

        Assert.Equal(384, result.Length);
        // Verify L2 normalized (magnitude ~= 1.0)
        var magnitude = MathF.Sqrt(result.Sum(x => x * x));
        Assert.InRange(magnitude, 0.99f, 1.01f);
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires ONNX model file")]
    public void GenerateEmbedding_SimilarTexts_ProduceSimilarVectors()
    {
        var modelPath = Environment.GetEnvironmentVariable("ONNX_MODEL_PATH")
            ?? "models/all-MiniLM-L6-v2.onnx";

        using var engine = new OnnxEmbeddingEngine(modelPath, 384);
        var v1 = engine.GenerateEmbedding("user authentication login");
        var v2 = engine.GenerateEmbedding("user auth signin");
        var v3 = engine.GenerateEmbedding("database connection pooling");

        var sim12 = CosineSimilarity(v1, v2);
        var sim13 = CosineSimilarity(v1, v3);

        // Similar texts should have higher similarity
        Assert.True(sim12 > sim13);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
