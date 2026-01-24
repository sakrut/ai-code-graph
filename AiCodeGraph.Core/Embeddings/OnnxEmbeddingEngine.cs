using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AiCodeGraph.Core.Embeddings;

public class OnnxEmbeddingEngine : IEmbeddingEngine
{
    private readonly InferenceSession _session;
    private readonly int _dimensions;
    private readonly int _maxTokens;
    private const int BertVocabSize = 30522;

    public int Dimensions => _dimensions;

    public OnnxEmbeddingEngine(string modelPath, int dimensions = 384, int maxTokens = 512)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("ONNX model not found.", modelPath);

        _dimensions = dimensions;
        _maxTokens = maxTokens;
        _session = new InferenceSession(modelPath);
    }

    internal OnnxEmbeddingEngine(InferenceSession session, int dimensions = 384, int maxTokens = 512)
    {
        _session = session;
        _dimensions = dimensions;
        _maxTokens = maxTokens;
    }

    public float[] GenerateEmbedding(string text)
    {
        var tokens = SimpleTokenize(text);

        var inputIds = new DenseTensor<long>(new[] { 1, tokens.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.Length });

        for (int i = 0; i < tokens.Length; i++)
        {
            inputIds[0, i] = tokens[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Mean pooling over token dimension
        var embedding = new float[_dimensions];
        var outputDims = Math.Min(_dimensions, output.Dimensions[2]);

        for (int d = 0; d < outputDims; d++)
        {
            float sum = 0;
            for (int t = 0; t < tokens.Length; t++)
                sum += output[0, t, d];
            embedding[d] = sum / tokens.Length;
        }

        // L2 normalize
        float norm = 0;
        for (int d = 0; d < embedding.Length; d++)
            norm += embedding[d] * embedding[d];
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (int d = 0; d < embedding.Length; d++)
                embedding[d] /= norm;
        }

        return embedding;
    }

    private long[] SimpleTokenize(string text)
    {
        // Basic tokenization: split by whitespace, hash to vocab range
        // For production use, a proper BPE/WordPiece tokenizer is needed
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokenCount = Math.Min(words.Length, _maxTokens);
        var tokens = new long[tokenCount];

        for (int i = 0; i < tokenCount; i++)
            tokens[i] = (long)(((uint)words[i].GetHashCode()) % BertVocabSize);

        return tokens;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
