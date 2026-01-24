using System.Text.Json.Nodes;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Mcp.Handlers;

namespace AiCodeGraph.Cli.Mcp;

public class McpServer
{
    private readonly string _dbPath;
    private StorageService? _storage;
    private VectorIndex? _vectorIndex;
    private readonly List<IMcpToolHandler> _handlers = new();

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
                var errorResponse = McpProtocolHelpers.CreateError(null, -32603, $"Internal error: {ex.Message}");
                Console.WriteLine(errorResponse.ToJsonString());
                Console.Out.Flush();
            }
        }
    }

    private async Task<JsonNode?> HandleMessage(JsonNode message, CancellationToken ct)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];

        if (id == null && method == "notifications/initialized")
            return null;

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolCall(message, id, ct),
            _ => id != null ? McpProtocolHelpers.CreateError(id, -32601, $"Method not found: {method}") : null
        };
    }

    private static JsonNode HandleInitialize(JsonNode? id)
    {
        return McpProtocolHelpers.CreateResult(id, new JsonObject
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

    private static JsonNode HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray();
        // Collect tool definitions from handler types (no storage needed for metadata)
        var dummyHandlers = new IMcpToolHandler[]
        {
            new ContextHandler(null!),
            new AnalysisHandler("", () => null, _ => { }, () => { }),
            new QueryHandler(null!),
            new SearchHandler(null!, () => null, _ => { }),
            new DuplicatesHandler(null!)
        };
        foreach (var handler in dummyHandlers)
        {
            foreach (var def in handler.GetToolDefinitions())
                tools.Add(def!.DeepClone());
        }
        return McpProtocolHelpers.CreateResult(id, new JsonObject { ["tools"] = tools });
    }

    private async Task<JsonNode> HandleToolCall(JsonNode message, JsonNode? id, CancellationToken ct)
    {
        var toolName = message["params"]?["name"]?.GetValue<string>();
        var args = message["params"]?["arguments"];

        if (toolName != "cg_analyze" && !File.Exists(_dbPath))
            return McpProtocolHelpers.CreateToolResult(id, $"Error: Database not found at {_dbPath}. Run 'ai-code-graph analyze' first.", true);

        if (toolName != "cg_analyze" && _storage == null)
        {
            _storage = new StorageService(_dbPath);
            await _storage.OpenAsync(ct);
        }

        InitializeHandlers();

        try
        {
            var handler = _handlers.FirstOrDefault(h => h.SupportedTools.Contains(toolName));
            if (handler == null)
                return McpProtocolHelpers.CreateToolResult(id, $"Unknown tool: {toolName}", true);

            var result = await handler.HandleAsync(toolName!, args, ct);
            return McpProtocolHelpers.CreateToolResult(id, result, false);
        }
        catch (Exception ex)
        {
            return McpProtocolHelpers.CreateToolResult(id, $"Error: {ex.Message}", true);
        }
    }

    private void InitializeHandlers()
    {
        if (_handlers.Count > 0) return;

        _handlers.Add(new ContextHandler(_storage!));
        _handlers.Add(new AnalysisHandler(
            _dbPath,
            () => _storage,
            s => _storage = s,
            () => _vectorIndex = null));
        _handlers.Add(new QueryHandler(_storage!));
        _handlers.Add(new SearchHandler(
            _storage!,
            () => _vectorIndex,
            idx => _vectorIndex = idx));
        _handlers.Add(new DuplicatesHandler(_storage!));
    }
}
