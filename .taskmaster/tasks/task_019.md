# Task ID: 19

**Title:** Implement MCP Server Mode

**Status:** done

**Dependencies:** 16 ✓

**Priority:** high

**Description:** Add `ai-code-graph mcp` subcommand that runs a JSON-RPC stdio MCP server exposing 4 tools: get_context, get_hotspots, search_code, and get_duplicates. The server handles the standard MCP lifecycle and returns compact text responses.

**Details:**

Implementation approach - add MCP server directly in AiCodeGraph.Cli with minimal dependencies:

1. **Create MCP protocol models** in `AiCodeGraph.Core/Mcp/` directory:
```csharp
// McpModels.cs
namespace AiCodeGraph.Core.Mcp;

public record JsonRpcRequest(string Jsonrpc, string Method, object? Params, object? Id);
public record JsonRpcResponse(string Jsonrpc, object? Result, JsonRpcError? Error, object? Id);
public record JsonRpcError(int Code, string Message, object? Data = null);

public record McpInitializeParams(McpClientInfo ClientInfo, string ProtocolVersion);
public record McpClientInfo(string Name, string? Version);
public record McpServerInfo(string Name, string Version);
public record McpInitializeResult(string ProtocolVersion, McpServerCapabilities Capabilities, McpServerInfo ServerInfo);
public record McpServerCapabilities(McpToolsCapability? Tools = null);
public record McpToolsCapability();

public record McpTool(string Name, string Description, McpToolInputSchema InputSchema);
public record McpToolInputSchema(string Type, Dictionary<string, McpPropertySchema> Properties, List<string>? Required = null);
public record McpPropertySchema(string Type, string? Description = null);

public record McpToolCallParams(string Name, Dictionary<string, object>? Arguments);
public record McpToolResult(List<McpContent> Content, bool? IsError = null);
public record McpContent(string Type, string Text);
```

2. **Create McpServer class** in `AiCodeGraph.Core/Mcp/McpServer.cs`:
```csharp
public class McpServer
{
    private readonly StorageService _storage;
    private readonly VectorIndex _vectorIndex;
    private bool _initialized = false;
    
    public McpServer(StorageService storage)
    {
        _storage = storage;
        _vectorIndex = new VectorIndex();
    }
    
    public async Task RunAsync(CancellationToken ct)
    {
        // Load embeddings for search
        var embeddings = await _storage.GetEmbeddingsAsync(ct);
        _vectorIndex.BuildIndex(embeddings);
        
        // Read JSON-RPC messages from stdin, write responses to stdout
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break; // EOF
            
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(line);
            var response = await HandleRequest(request, ct);
            if (response != null) // Don't respond to notifications
            {
                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await writer.WriteLineAsync(json);
            }
        }
    }
    
    private async Task<JsonRpcResponse?> HandleRequest(JsonRpcRequest request, CancellationToken ct)
    {
        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "initialized" => null, // notification, no response
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolCall(request, ct),
            _ => new JsonRpcResponse("2.0", null, new JsonRpcError(-32601, $"Method not found: {request.Method}"), request.Id)
        };
    }
}
```

3. **Implement the 4 MCP tools**:
   - `get_context`: Reuse the same logic as the CLI context command. Input: `{ "method": "pattern" }`. Returns compact text.
   - `get_hotspots`: Input: `{ "top": 10 }`. Returns compact table of top N hotspots.
   - `search_code`: Input: `{ "query": "text" }`. Uses HashEmbeddingEngine to generate query vector, VectorIndex to search. Returns top matches.
   - `get_duplicates`: Input: `{ "method": "optional-pattern", "threshold": 0.7 }`. Returns clone pairs.

4. **Register the CLI command** in Program.cs:
```csharp
var mcpCommand = new Command("mcp", "Run as MCP server (JSON-RPC over stdio)") { dbOption };
mcpCommand.SetAction(async (parseResult, cancellationToken) => {
    var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
    if (!File.Exists(dbPath)) {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}.");
        Environment.ExitCode = 1;
        return;
    }
    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);
    var server = new McpServer(storage);
    await server.RunAsync(cancellationToken);
});
rootCommand.Add(mcpCommand);
```

5. **MCP tool definitions** (returned by tools/list):
   - `get_context`: name="get_context", description="Get combined method context (complexity, callers, callees, cluster, duplicates)", params: method (string, required)
   - `get_hotspots`: name="get_hotspots", description="Top N complexity hotspots", params: top (int, optional, default 10)
   - `search_code`: name="search_code", description="Semantic code search by natural language query", params: query (string, required), top (int, optional, default 5)
   - `get_duplicates`: name="get_duplicates", description="Get code clone pairs", params: method (string, optional), threshold (number, optional, default 0.7)

6. **Key design decisions**:
   - Use line-delimited JSON-RPC (one message per line) as per MCP stdio transport spec
   - Return compact text content (not JSON blobs) to save tokens
   - No background threads - purely request/response
   - Log errors to stderr (not stdout, which is the protocol channel)
   - Support graceful shutdown on EOF or SIGTERM

**Test Strategy:**

1. Unit tests for McpServer:
   - Test initialize handshake returns correct capabilities and protocol version
   - Test tools/list returns all 4 tools with correct schemas
   - Test each tool call with valid inputs returns expected compact text format
   - Test error handling for invalid method names, missing required params
   - Test EOF handling (graceful shutdown)
2. Integration tests:
   - Spawn the MCP server process, send initialize sequence, call each tool, verify responses
   - Use the TestSolution fixture database for realistic data
   - Test with piped stdin/stdout using Process.Start
3. Protocol compliance:
   - Verify JSON-RPC 2.0 format (jsonrpc field, id field, result/error)
   - Verify notifications (initialized) don't produce responses
   - Test with unknown methods returns -32601 error
4. Add a sample `.mcp.json` configuration showing how to register the server with Claude Code
