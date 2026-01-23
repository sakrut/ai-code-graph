namespace AiCodeGraph.Core.Embeddings;

public interface IEmbeddingEngine : IDisposable
{
    int Dimensions { get; }
    float[] GenerateEmbedding(string text);
}
