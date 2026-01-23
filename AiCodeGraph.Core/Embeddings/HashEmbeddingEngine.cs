using System.Security.Cryptography;
using System.Text;

namespace AiCodeGraph.Core.Embeddings;

/// <summary>
/// Generates embeddings using feature hashing (hashing trick).
/// Maps token n-grams to fixed-dimension vectors deterministically.
/// Can be replaced with an ONNX-based engine for higher quality embeddings.
/// </summary>
public class HashEmbeddingEngine : IEmbeddingEngine
{
    public int Dimensions { get; }

    public HashEmbeddingEngine(int dimensions = 384)
    {
        Dimensions = dimensions;
    }

    public float[] GenerateEmbedding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[Dimensions];

        var vector = new float[Dimensions];
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Unigrams
        foreach (var token in tokens)
        {
            AddToVector(vector, token, 1.0f);
        }

        // Bigrams for context
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var bigram = $"{tokens[i]}_{tokens[i + 1]}";
            AddToVector(vector, bigram, 0.5f);
        }

        // L2 normalize
        Normalize(vector);
        return vector;
    }

    public void Dispose() { }

    private void AddToVector(float[] vector, string token, float weight)
    {
        // Use two hash functions for signed random projection
        var hash1 = GetStableHash(token, 0);
        var hash2 = GetStableHash(token, 1);

        var index = (int)(((uint)hash1) % Dimensions);
        var sign = (hash2 & 1) == 0 ? 1.0f : -1.0f;

        vector[index] += sign * weight;
    }

    private static int GetStableHash(string input, int seed)
    {
        var bytes = Encoding.UTF8.GetBytes($"{seed}:{input}");
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }

    private static void Normalize(float[] vector)
    {
        float magnitude = 0;
        for (int i = 0; i < vector.Length; i++)
            magnitude += vector[i] * vector[i];

        magnitude = MathF.Sqrt(magnitude);
        if (magnitude > 0)
        {
            for (int i = 0; i < vector.Length; i++)
                vector[i] /= magnitude;
        }
    }
}
