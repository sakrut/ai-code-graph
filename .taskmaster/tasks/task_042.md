# Task ID: 42

**Title:** OpenAI Embedding Engine Adapter

**Status:** done

**Dependencies:** 30 ✓, 31 ✓, 32 ✓, 33 ✓, 35 ✓, 37 ✓, 38 ✓, 39 ✓, 40 ✓, 41 ✓

**Priority:** high

**Description:** Implement IEmbeddingEngine using OpenAI's text-embedding-3-small/large API with batching (100 texts per call) and exponential backoff for rate limits.

**Details:**

Create new file: AiCodeGraph.Core/Embeddings/OpenAiEmbeddingEngine.cs

```csharp
using System.Net.Http.Json;
using System.Text.Json;
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
        _model = model;
        _dimensions = dimensions;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }
    
    public float[] GenerateEmbedding(string text)
    {
        // Synchronous wrapper for single text
        return GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
    }
    
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var batch = await GenerateBatchAsync(new[] { text });
        return batch[0];
    }
    
    public async Task<List<float[]>> GenerateBatchAsync(IReadOnlyList<string> texts)
    {
        var results = new List<float[]>();
        
        for (int i = 0; i < texts.Count; i += MaxBatchSize)
        {
            var batch = texts.Skip(i).Take(MaxBatchSize).ToList();
            var embeddings = await CallApiWithRetry(batch);
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
                var request = new { input = texts, model = _model, dimensions = _dimensions };
                var response = await _httpClient.PostAsJsonAsync(ApiUrl, request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(delay);
                    continue;
                }
                
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
                return result!.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
        throw new InvalidOperationException("Failed to get embeddings after retries");
    }
    
    public void Dispose() => _httpClient.Dispose();
    
    private record EmbeddingResponse(List<EmbeddingData> Data);
    private record EmbeddingData(int Index, float[] Embedding);
}
```

Add `System.Net.Http.Json` package reference if not already present.

**Test Strategy:**

Create OpenAiEmbeddingEngineTests.cs using mocked HttpClient (HttpMessageHandler mock). Test: (1) Single embedding returns correct dimensions. (2) Batch > 100 splits into multiple API calls. (3) Rate limit (429) triggers retry with backoff. (4) Max retries exceeded throws. (5) API error response handled. (6) Verify request format matches OpenAI spec. (7) Dispose cleans up HttpClient.

## Subtasks

### 42.1. Create OpenAiEmbeddingEngine class with HttpClient setup and IEmbeddingEngine implementation

**Status:** pending  
**Dependencies:** None  

Create the OpenAiEmbeddingEngine.cs file in AiCodeGraph.Core/Embeddings/ implementing the IEmbeddingEngine interface with HttpClient configuration, API URL constant, constructor accepting API key/model/dimensions parameters, and the synchronous GenerateEmbedding method wrapping async logic.

**Details:**

Create new file AiCodeGraph.Core/Embeddings/OpenAiEmbeddingEngine.cs. Implement IEmbeddingEngine interface with Dimensions property returning configured dimension count. Constructor takes apiKey (required), model (default 'text-embedding-3-small'), and dimensions (default 384). Configure HttpClient with Authorization Bearer header. Define constants: ApiUrl = 'https://api.openai.com/v1/embeddings', MaxBatchSize = 100, MaxRetries = 3. Implement synchronous GenerateEmbedding(string text) using .GetAwaiter().GetResult() on async path. Implement IDisposable to dispose HttpClient. Add System.Net.Http.Json package reference to AiCodeGraph.Core.csproj if not already present. Define internal record types EmbeddingResponse and EmbeddingData for JSON deserialization.

### 42.2. Implement batching logic with request/response JSON serialization

**Status:** pending  
**Dependencies:** 42.1  

Implement the GenerateBatchAsync method that splits input texts into chunks of MaxBatchSize (100), sends each chunk to the OpenAI API with proper JSON serialization, and reassembles results in correct order.

**Details:**

Implement GenerateEmbeddingAsync(string text) that calls GenerateBatchAsync with single-element list. Implement GenerateBatchAsync(IReadOnlyList<string> texts) that iterates through texts in chunks of MaxBatchSize (100), calling the API for each batch and aggregating results into a single List<float[]>. Build JSON request payload with fields: input (list of strings), model (configured model name), dimensions (configured dimension count). Parse API response using System.Net.Http.Json's ReadFromJsonAsync<EmbeddingResponse>. Order response embeddings by their Index field to ensure correct alignment with input order. Use PostAsJsonAsync for request serialization.

### 42.3. Add exponential backoff retry logic for rate limits and transient errors

**Status:** pending  
**Dependencies:** 42.1, 42.2  

Implement the CallApiWithRetry method with exponential backoff handling for HTTP 429 (Too Many Requests) responses and transient HttpRequestException errors, with configurable max retries.

**Details:**

Implement private CallApiWithRetry(List<string> texts) method. Loop up to MaxRetries (3) attempts. On HttpStatusCode.TooManyRequests (429), calculate delay as TimeSpan.FromSeconds(Math.Pow(2, attempt)) and await Task.Delay before continuing to next attempt. On HttpRequestException when attempt < MaxRetries, apply same exponential backoff delay. Call response.EnsureSuccessStatusCode() for non-429 error responses to throw on 4xx/5xx. After exhausting all retries, throw InvalidOperationException with descriptive message. Ensure successful responses are parsed and returned immediately without unnecessary delays.

### 42.4. Write unit tests with mocked HttpMessageHandler

**Status:** pending  
**Dependencies:** 42.1, 42.2, 42.3  

Create comprehensive unit tests in OpenAiEmbeddingEngineTests.cs using a mocked HttpMessageHandler to verify batch splitting, retry behavior, error handling, and correct response parsing without making real API calls.

**Details:**

Create AiCodeGraph.Tests/Embeddings/OpenAiEmbeddingEngineTests.cs. Build a MockHttpMessageHandler that can be configured with queued responses or response functions. Test cases: (1) Single embedding returns float[] of correct dimensions. (2) Batch of 150 texts splits into two API calls verified by request count. (3) Rate limit 429 triggers retry - mock returns 429 then 200. (4) Max retries exceeded throws InvalidOperationException - mock returns 429 on all attempts. (5) API error (500) with retries exhausted throws. (6) Successful response with out-of-order indices is reordered correctly. (7) Empty input list returns empty results. (8) Verify Authorization header contains Bearer token. (9) Verify request body contains correct model and dimensions fields. Use System.Text.Json to build mock response JSON matching OpenAI's embedding response format.
