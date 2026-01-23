using System.Text.Json;
using System.Text.Json.Nodes;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp;

public class McpServer
{
    private readonly string _dbPath;
    private StorageService? _storage;



    public McpServer(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line);
                if (request == null) continue;

                var response = await HandleMessage(request, cancellationToken);
                if (response != null)
                {
                    Console.WriteLine(response.ToJsonString());
                    Console.Out.Flush();
                }
            }
            catch (Exception ex)
            {
                var errorResponse = CreateError(null, -32603, $"Internal error: {ex.Message}");
                Console.WriteLine(errorResponse.ToJsonString());
                Console.Out.Flush();
            }
        }
    }

    private async Task<JsonNode?> HandleMessage(JsonNode message, CancellationToken ct)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];

        // Notifications (no id) - don't respond
        if (id == null && method == "notifications/initialized")
            return null;

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolCall(message, id, ct),
            _ => id != null ? CreateError(id, -32601, $"Method not found: {method}") : null
        };
    }

    private JsonNode HandleInitialize(JsonNode? id)
    {
        return CreateResult(id, new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ai-code-graph",
                ["version"] = "0.1.0"
            }
        });
    }

    private JsonNode HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray
        {
            CreateToolDef("get_context",
                "Get compact method context: complexity, callers, callees, cluster, duplicates",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern to search for" }
                    },
                    ["required"] = new JsonArray { "method" }
                }),
            CreateToolDef("get_hotspots",
                "Get top complexity hotspots",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 },
                        ["threshold"] = new JsonObject { ["type"] = "integer", ["description"] = "Minimum complexity threshold" }
                    }
                }),
            CreateToolDef("search_code",
                "Search code by natural language intent",
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
            CreateToolDef("get_duplicates",
                "Get detected code clones, optionally for a specific method",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: method name to find duplicates for" },
                        ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 }
                    }
                })
        };

        return CreateResult(id, new JsonObject { ["tools"] = tools });
    }

    private async Task<JsonNode> HandleToolCall(JsonNode message, JsonNode? id, CancellationToken ct)
    {
        var toolName = message["params"]?["name"]?.GetValue<string>();
        var args = message["params"]?["arguments"];

        if (!File.Exists(_dbPath))
            return CreateToolResult(id, $"Error: Database not found at {_dbPath}. Run 'ai-code-graph analyze' first.", true);

        if (_storage == null)
        {
            _storage = new StorageService(_dbPath);
            await _storage.OpenAsync(ct);
        }

        try
        {
            var result = toolName switch
            {
                "get_context" => await ToolGetContext(args, ct),
                "get_hotspots" => await ToolGetHotspots(args, ct),
                "search_code" => await ToolSearchCode(args, ct),
                "get_duplicates" => await ToolGetDuplicates(args, ct),
                _ => $"Unknown tool: {toolName}"
            };

            return CreateToolResult(id, result, false);
        }
        catch (Exception ex)
        {
            return CreateToolResult(id, $"Error: {ex.Message}", true);
        }
    }

    private async Task<string> ToolGetContext(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";

        var matches = await _storage!.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches.Count == 1
            ? matches[0].Id
            : matches.FirstOrDefault(m => m.FullName.Contains(method, StringComparison.OrdinalIgnoreCase)).Id ?? matches[0].Id;

        var info = await _storage.GetMethodInfoAsync(targetId, ct);
        if (info == null) return "Method info not found";

        var lines = new List<string>();
        lines.Add($"Method: {info.Value.FullName}");
        if (info.Value.FilePath != null)
            lines.Add($"File: {info.Value.FilePath}:{info.Value.StartLine}");

        var metrics = await _storage.GetMethodMetricsAsync(targetId, ct);
        if (metrics != null)
            lines.Add($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");

        var callers = await _storage.GetCallersAsync(targetId, ct);
        if (callers.Count > 0)
        {
            var names = new List<string>();
            foreach (var cid in callers.Take(5))
            {
                var ci = await _storage.GetMethodInfoAsync(cid, ct);
                names.Add(ci?.Name ?? cid);
            }
            lines.Add($"Callers ({callers.Count}): {string.Join(", ", names)}");
        }

        var callees = await _storage.GetCalleesAsync(targetId, ct);
        if (callees.Count > 0)
        {
            var names = new List<string>();
            foreach (var cid in callees.Take(5))
            {
                var ci = await _storage.GetMethodInfoAsync(cid, ct);
                names.Add(ci?.Name ?? cid);
            }
            lines.Add($"Callees ({callees.Count}): {string.Join(", ", names)}");
        }

        var cluster = await _storage.GetMethodClusterAsync(targetId, ct);
        if (cluster != null)
            lines.Add($"Cluster: \"{cluster.Value.Label}\" ({cluster.Value.MemberCount} members, cohesion: {cluster.Value.Cohesion:F2})");

        var dupes = await _storage.GetMethodDuplicatesAsync(targetId, ct);
        if (dupes.Count > 0)
        {
            var dupeStrs = dupes.Take(3).Select(d =>
            {
                var parenIdx = d.OtherFullName.IndexOf('(');
                var nameOnly = parenIdx >= 0 ? d.OtherFullName[..parenIdx] : d.OtherFullName;
                var parts = nameOnly.Split('.');
                var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
                return $"{shortName} ({d.HybridScore:F2})";
            });
            lines.Add($"Duplicates ({dupes.Count}): {string.Join(", ", dupeStrs)}");
        }

        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetHotspots(JsonNode? args, CancellationToken ct)
    {
        var top = args?["top"]?.GetValue<int>() ?? 10;
        var threshold = args?["threshold"]?.GetValue<int>();

        var hotspots = await _storage!.GetHotspotsWithThresholdAsync(top, threshold, ct);
        if (hotspots.Count == 0) return "No hotspots found.";

        var lines = new List<string> { $"{"Method",-50} {"CC",4} {"LOC",4} {"Nest",4}" };
        foreach (var h in hotspots)
        {
            var name = h.FullName.Length > 50 ? h.FullName[..47] + "..." : h.FullName;
            lines.Add($"{name,-50} {h.Complexity,4} {h.Loc,4} {h.Nesting,4}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> ToolSearchCode(JsonNode? args, CancellationToken ct)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query)) return "Error: 'query' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 5;

        var embeddings = await _storage!.GetEmbeddingsAsync(ct);
        if (embeddings.Count == 0) return "No embeddings in database.";

        using var engine = new HashEmbeddingEngine();
        var queryVector = engine.GenerateEmbedding(query);
        var index = new VectorIndex();
        index.BuildIndex(embeddings);
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

    private async Task<string> ToolGetDuplicates(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        var top = args?["top"]?.GetValue<int>() ?? 10;

        if (!string.IsNullOrEmpty(method))
        {
            var matches = await _storage!.SearchMethodsAsync(method, ct);
            if (matches.Count == 0) return $"Method not found: '{method}'";
            var targetId = matches[0].Id;
            var dupes = await _storage.GetMethodDuplicatesAsync(targetId, ct);
            if (dupes.Count == 0) return "No duplicates found for this method.";

            var lines = new List<string>();
            foreach (var d in dupes.Take(top))
                lines.Add($"{d.Type,-8} {d.HybridScore:F2}  {d.OtherFullName}");
            return string.Join("\n", lines);
        }
        else
        {
            var clones = await _storage!.GetClonePairsAsync(cancellationToken: ct);
            if (clones.Count == 0) return "No duplicates detected.";

            var lines = new List<string>();
            foreach (var c in clones.Take(top))
                lines.Add($"{c.Type,-8} {c.HybridScore:F2}  {c.MethodIdA} <-> {c.MethodIdB}");
            return string.Join("\n", lines);
        }
    }

    private static JsonObject CreateToolDef(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonNode CreateResult(JsonNode? id, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        };
    }

    private static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static JsonNode CreateToolResult(JsonNode? id, string text, bool isError)
    {
        return CreateResult(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        });
    }
}
