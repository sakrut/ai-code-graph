using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AiCodeGraph.Core.Embeddings;

public class OpenAiEmbeddingEngine : IEmbeddingEngine
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimensions;
    private const string ApiUrl = "https://api.openai.com/v1/embeddings";
    private const int MaxBatchSize = 100;
    private const int MaxRetries = 3;

    public int Dimensions => _dimensions;

    public OpenAiEmbeddingEngine(string apiKey, string model = "text-embedding-3-small", int dimensions = 384)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _model = model;
        _dimensions = dimensions;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public OpenAiEmbeddingEngine(HttpClient httpClient, string model = "text-embedding-3-small", int dimensions = 384)
    {
        _model = model;
        _dimensions = dimensions;
        _httpClient = httpClient;
    }

    public float[] GenerateEmbedding(string text)
    {
        return GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var batch = await GenerateBatchAsync(new[] { text }).ConfigureAwait(false);
        return batch[0];
    }

    public async Task<List<float[]>> GenerateBatchAsync(IReadOnlyList<string> texts)
    {
        var results = new List<float[]>();

        for (int i = 0; i < texts.Count; i += MaxBatchSize)
        {
            var batch = texts.Skip(i).Take(MaxBatchSize).ToList();
            var embeddings = await CallApiWithRetry(batch).ConfigureAwait(false);
            results.AddRange(embeddings);
        }

        return results;
    }

    private async Task<List<float[]>> CallApiWithRetry(List<string> texts)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var request = new EmbeddingRequest(_model, texts, _dimensions);
                var response = await _httpClient.PostAsJsonAsync(ApiUrl, request).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt == MaxRetries) break;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(delay).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>().ConfigureAwait(false);
                return result!.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Failed to get embeddings after retries.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] List<string> Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
