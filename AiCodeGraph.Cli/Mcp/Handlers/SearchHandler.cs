using System.Text.Json.Nodes;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class SearchHandler : IMcpToolHandler
{
    private readonly StorageService _storage;
    private readonly Func<VectorIndex?> _getVectorIndex;
    private readonly Action<VectorIndex> _setVectorIndex;

    public SearchHandler(StorageService storage, Func<VectorIndex?> getVectorIndex, Action<VectorIndex> setVectorIndex)
    {
        _storage = storage;
        _getVectorIndex = getVectorIndex;
        _setVectorIndex = setVectorIndex;
    }

    public IReadOnlyList<string> SupportedTools { get; } = new[]
    {
        "cg_token_search", "cg_semantic_search", "cg_get_similar"
    };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_token_search",
            "Search code by token overlap",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search query (uses method identifier tokens)" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 5 }
                },
                ["required"] = new JsonArray { "query" }
            }),
        McpProtocolHelpers.CreateToolDef("cg_semantic_search",
            "Search code by semantic meaning using LLM embeddings",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Natural language search query" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 }
                },
                ["required"] = new JsonArray { "query" }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_similar",
            "Find methods with similar semantic intent",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 }
                },
                ["required"] = new JsonArray { "method" }
            })
    };

    public Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct)
    {
        return toolName switch
        {
            "cg_token_search" => TokenSearch(args, ct),
            "cg_semantic_search" => SemanticSearch(args, ct),
            "cg_get_similar" => GetSimilar(args, ct),
            _ => Task.FromResult($"Unknown tool: {toolName}")
        };
    }

    private VectorIndex EnsureVectorIndex(List<(string MethodId, float[] Vector)> embeddings)
    {
        var index = _getVectorIndex();
        if (index != null) return index;
        index = new VectorIndex();
        index.BuildIndex(embeddings);
        _setVectorIndex(index);
        return index;
    }

    private async Task<string> TokenSearch(JsonNode? args, CancellationToken ct)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query)) return "Error: 'query' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 5;

        var embeddings = await _storage.GetEmbeddingsAsync(ct);
        if (embeddings.Count == 0) return "No embeddings in database.";

        using var engine = new HashEmbeddingEngine();
        var queryVector = engine.GenerateEmbedding(query);
        var index = EnsureVectorIndex(embeddings);
        var results = index.Search(queryVector, top);

        if (results.Count == 0) return "No results found.";

        var lines = new List<string>();
        foreach (var (methodId, score) in results)
        {
            var info = await _storage.GetMethodInfoAsync(methodId, ct);
            var name = info?.FullName ?? methodId;
            var file = info?.FilePath != null ? $"  {Path.GetFileName(info.Value.FilePath)}:{info.Value.StartLine}" : "";
            lines.Add($"{score:F3}  {name}{file}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> SemanticSearch(JsonNode? args, CancellationToken ct)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query)) return "Error: 'query' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 10;

        var allEmbeddings = await _storage.GetEmbeddingsAsync(ct);
        if (allEmbeddings.Count == 0)
            return "No embeddings found. Run 'analyze' first.";

        var engineType = await _storage.GetMetadataAsync("embedding_engine", ct) ?? "hash";
        var modelName = await _storage.GetMetadataAsync("embedding_model", ct);
        var dimStr = await _storage.GetMetadataAsync("embedding_dimensions", ct);
        var dimensions = int.TryParse(dimStr, out var d) ? d : 384;

        IEmbeddingEngine engine = engineType switch
        {
            "openai" => CreateOpenAiEngine(modelName, dimensions),
            "onnx" => CreateOnnxEngine(modelName, dimensions),
            _ => new HashEmbeddingEngine()
        };

        using (engine)
        {
            var queryVector = engine.GenerateEmbedding(query);
            var index = EnsureVectorIndex(allEmbeddings);
            var searchResults = index.Search(queryVector, top);

            var lines = new List<string>();
            if (engineType == "hash")
                lines.Add("Note: Using hash-based embeddings (token overlap). Re-analyze with --embedding-engine openai for semantic search.");
            lines.Add($"Results for: \"{query}\" (engine: {engineType})");
            lines.Add("");

            foreach (var (id, score) in searchResults)
            {
                var info = await _storage.GetMethodInfoAsync(id, ct);
                lines.Add($"  [{score:F4}] {info?.FullName ?? id}");
                if (info?.FilePath != null) lines.Add($"         {info.Value.FilePath}:{info.Value.StartLine}");
            }
            return string.Join("\n", lines);
        }
    }

    private async Task<string> GetSimilar(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 10;

        var matches = await _storage.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches[0].Id;
        var allEmbeddings = await _storage.GetEmbeddingsAsync(ct);
        if (allEmbeddings.Count == 0) return "No embeddings in database.";

        var targetEmbedding = allEmbeddings.FirstOrDefault(e => e.MethodId == targetId);
        if (targetEmbedding.Vector == null) return $"No embedding found for '{method}'";

        var index = EnsureVectorIndex(allEmbeddings);
        var results = index.Search(targetEmbedding.Vector, top + 1)
            .Where(r => r.Id != targetId)
            .Take(top)
            .ToList();

        if (results.Count == 0) return "No similar methods found.";

        var lines = new List<string> { $"Similar to: {matches[0].FullName}", "" };
        foreach (var (id, score) in results)
        {
            var info = await _storage.GetMethodInfoAsync(id, ct);
            lines.Add($"  {score:F3}  {info?.FullName ?? id}");
        }
        return string.Join("\n", lines);
    }

    private static IEmbeddingEngine CreateOpenAiEngine(string? model, int dimensions)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            return new HashEmbeddingEngine();
        return new OpenAiEmbeddingEngine(apiKey, model ?? "text-embedding-3-small", dimensions);
    }

    private static IEmbeddingEngine CreateOnnxEngine(string? modelPath, int dimensions)
    {
        var path = modelPath ?? "./models/all-MiniLM-L6-v2.onnx";
        if (!File.Exists(path))
            return new HashEmbeddingEngine();
        return new OnnxEmbeddingEngine(path, dimensions);
    }
}
