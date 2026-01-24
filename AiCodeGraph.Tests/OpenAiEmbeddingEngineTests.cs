using System.Net;
using System.Text;
using System.Text.Json;
using AiCodeGraph.Core.Embeddings;

namespace AiCodeGraph.Tests;

public class OpenAiEmbeddingEngineTests
{
    private static HttpClient CreateMockClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var mockHandler = new MockHttpMessageHandler(handler);
        return new HttpClient(mockHandler);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ReturnCorrectDimensions()
    {
        var embedding = new float[384];
        Array.Fill(embedding, 0.5f);

        var client = CreateMockClient(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            Assert.Contains("test-embedding-3-small", body);
            return CreateEmbeddingResponse(new[] { embedding });
        });

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);
        var result = await engine.GenerateEmbeddingAsync("hello world");

        Assert.Equal(384, result.Length);
        Assert.Equal(0.5f, result[0]);
    }

    [Fact]
    public async Task GenerateBatchAsync_SplitsIntoBatches()
    {
        int callCount = 0;
        var embedding = new float[384];

        var client = CreateMockClient(async req =>
        {
            Interlocked.Increment(ref callCount);
            var body = await req.Content!.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var inputCount = doc.RootElement.GetProperty("input").GetArrayLength();
            var embeddings = Enumerable.Range(0, inputCount).Select(_ => (float[])embedding.Clone()).ToArray();
            return CreateEmbeddingResponse(embeddings);
        });

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);
        var texts = Enumerable.Range(0, 250).Select(i => $"text {i}").ToList();
        var results = await engine.GenerateBatchAsync(texts);

        Assert.Equal(250, results.Count);
        Assert.Equal(3, callCount); // 100 + 100 + 50
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RetriesOnRateLimit()
    {
        int callCount = 0;
        var embedding = new float[384];

        var client = CreateMockClient(_ =>
        {
            var count = Interlocked.Increment(ref callCount);
            if (count == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            return Task.FromResult(CreateEmbeddingResponse(new[] { embedding }));
        });

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);
        var result = await engine.GenerateEmbeddingAsync("test");

        Assert.Equal(384, result.Length);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ThrowsAfterMaxRetries()
    {
        var client = CreateMockClient(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.GenerateEmbeddingAsync("test"));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RetriesOnHttpError()
    {
        int callCount = 0;
        var embedding = new float[384];

        var client = CreateMockClient(_ =>
        {
            var count = Interlocked.Increment(ref callCount);
            if (count == 1)
                throw new HttpRequestException("Connection refused");
            return Task.FromResult(CreateEmbeddingResponse(new[] { embedding }));
        });

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);
        var result = await engine.GenerateEmbeddingAsync("test");

        Assert.Equal(384, result.Length);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_VerifyRequestFormat()
    {
        var embedding = new float[256];

        var client = CreateMockClient(async req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("api.openai.com", req.RequestUri!.Host);

            var body = await req.Content!.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            Assert.Equal("text-embedding-3-large", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal(256, doc.RootElement.GetProperty("dimensions").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("input").GetArrayLength());

            return CreateEmbeddingResponse(new[] { embedding });
        });

        using var engine = new OpenAiEmbeddingEngine(client, "text-embedding-3-large", 256);
        await engine.GenerateEmbeddingAsync("hello");
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => new OpenAiEmbeddingEngine(""));
        Assert.Throws<ArgumentException>(() => new OpenAiEmbeddingEngine((string)null!));
    }

    [Fact]
    public void Dimensions_ReturnsConfiguredValue()
    {
        var client = CreateMockClient(_ => Task.FromResult(new HttpResponseMessage()));
        using var engine = new OpenAiEmbeddingEngine(client, "test", 512);
        Assert.Equal(512, engine.Dimensions);
    }

    [Fact]
    public void GenerateEmbedding_Synchronous_Works()
    {
        var embedding = new float[384];
        Array.Fill(embedding, 1.0f);

        var client = CreateMockClient(_ =>
            Task.FromResult(CreateEmbeddingResponse(new[] { embedding })));

        using var engine = new OpenAiEmbeddingEngine(client, "test-embedding-3-small", 384);
        var result = engine.GenerateEmbedding("hello");

        Assert.Equal(384, result.Length);
        Assert.Equal(1.0f, result[0]);
    }

    [Fact]
    public async Task GenerateBatchAsync_OrdersByIndex()
    {
        var client = CreateMockClient(_ =>
        {
            // Return embeddings in reverse order
            var response = new
            {
                data = new[]
                {
                    new { index = 2, embedding = new float[] { 3f } },
                    new { index = 0, embedding = new float[] { 1f } },
                    new { index = 1, embedding = new float[] { 2f } }
                }
            };
            var json = JsonSerializer.Serialize(response);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var engine = new OpenAiEmbeddingEngine(client, "test", 1);
        var results = await engine.GenerateBatchAsync(new[] { "a", "b", "c" });

        Assert.Equal(1f, results[0][0]);
        Assert.Equal(2f, results[1][0]);
        Assert.Equal(3f, results[2][0]);
    }

    private static HttpResponseMessage CreateEmbeddingResponse(float[][] embeddings)
    {
        var data = embeddings.Select((e, i) => new { index = i, embedding = e }).ToArray();
        var response = new { data };
        var json = JsonSerializer.Serialize(response);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
