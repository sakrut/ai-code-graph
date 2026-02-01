# Task ID: 52

**Title:** Refactor McpServer.cs - Split God Class into Handler Classes

**Status:** done

**Dependencies:** 6 ✓, 8 ✓, 25 ✓, 41 ✓

**Priority:** medium

**Description:** Split the monolithic McpServer.cs (1253 lines, 17 tool handlers, CC=59 in ToolGetContext) into separate handler classes per tool category and decompose the ToolGetContext method into focused sub-methods for each context section.

**Details:**

## Current State

`AiCodeGraph.Cli/Mcp/McpServer.cs` is a single 1253-line class containing:
- MCP protocol handling (RunAsync, HandleMessage, HandleInitialize)
- Tool list definition (HandleToolsList with 17 tool definitions)
- 17 tool handler methods (ToolGetContext, ToolGetHotspots, ToolSearchCode, etc.)
- Shared state (_storage, _vectorIndex, _dbPath)
- Utility methods (CreateToolDef, CreateResult, CreateError, CreateToolResult, FormatAge, CountMethodsInNamespace/Type)
- Embedding engine factory methods

## Phase 1: Decompose ToolGetContext (CC=59)

Extract the ToolGetContext method (lines 335-468) into focused sub-methods. Create a new class `AiCodeGraph.Cli/Mcp/Handlers/ContextHandler.cs`:

```csharp
namespace AiCodeGraph.Cli.Mcp.Handlers;

public class ContextHandler
{
    private readonly StorageService _storage;

    public ContextHandler(StorageService storage) => _storage = storage;

    public async Task<string> HandleAsync(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";

        var (targetId, info) = await ResolveMethodAsync(method, ct);
        if (info == null) return $"Method not found: '{method}'";

        var lines = new List<string>();
        AppendMethodHeader(lines, info.Value);
        await AppendMetricsAsync(lines, targetId, ct);
        await AppendCallersAsync(lines, targetId, ct);
        await AppendCalleesAsync(lines, targetId, ct);
        await AppendClusterInfoAsync(lines, targetId, ct);
        await AppendRecentClusterActivityAsync(lines, targetId, ct);
        await AppendDuplicatesAsync(lines, targetId, ct);
        await AppendTestCoverageAsync(lines, info.Value.Name, ct);
        return string.Join("\n", lines);
    }

    private async Task<(string Id, MethodInfo? Info)> ResolveMethodAsync(string method, CancellationToken ct) { ... }
    private void AppendMethodHeader(List<string> lines, MethodInfo info) { ... }
    private async Task AppendMetricsAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendCallersAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendCalleesAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendClusterInfoAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendRecentClusterActivityAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendDuplicatesAsync(List<string> lines, string targetId, CancellationToken ct) { ... }
    private async Task AppendTestCoverageAsync(List<string> lines, string methodName, CancellationToken ct) { ... }
}
```

## Phase 2: Extract Handler Classes by Category

Create the following handler classes in `AiCodeGraph.Cli/Mcp/Handlers/`:

1. **`ContextHandler.cs`** - `cg_get_context` (decomposed as above)
2. **`AnalysisHandler.cs`** - `cg_analyze`, `cg_churn`, `cg_coupling`, `cg_diff`, `cg_get_drift`
3. **`QueryHandler.cs`** - `cg_get_hotspots`, `cg_get_callgraph`, `cg_get_tree`, `cg_dead_code`, `cg_get_impact`
4. **`SearchHandler.cs`** - `cg_token_search`, `cg_semantic_search`, `cg_get_similar`
5. **`DuplicatesHandler.cs`** - `cg_get_duplicates`, `cg_get_clusters`, `cg_export_graph`

Each handler receives `StorageService` and optionally `VectorIndex` (for search handlers) via constructor injection.

## Phase 3: Create IMcpToolHandler Interface

```csharp
namespace AiCodeGraph.Cli.Mcp;

public interface IMcpToolHandler
{
    IReadOnlyList<string> SupportedTools { get; }
    Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct);
}
```

## Phase 4: Refactor McpServer to Dispatcher

The McpServer class becomes a thin dispatcher:

```csharp
public class McpServer
{
    private readonly string _dbPath;
    private StorageService? _storage;
    private readonly List<IMcpToolHandler> _handlers = new();

    public McpServer(string dbPath) { _dbPath = dbPath; }

    private void InitializeHandlers()
    {
        var vectorIndex = new Lazy<VectorIndex>();
        _handlers.Add(new ContextHandler(_storage!));
        _handlers.Add(new AnalysisHandler(_storage!, _dbPath));
        _handlers.Add(new QueryHandler(_storage!));
        _handlers.Add(new SearchHandler(_storage!, vectorIndex));
        _handlers.Add(new DuplicatesHandler(_storage!));
    }

    private async Task<JsonNode> HandleToolCall(JsonNode message, JsonNode? id, CancellationToken ct)
    {
        // ... db init logic ...
        var handler = _handlers.FirstOrDefault(h => h.SupportedTools.Contains(toolName));
        if (handler == null) return CreateToolResult(id, $"Unknown tool: {toolName}", true);
        var result = await handler.HandleAsync(toolName, args, ct);
        return CreateToolResult(id, result, false);
    }
}
```

## Phase 5: Extract Tool Definitions

Move the `HandleToolsList` content to a static `McpToolDefinitions` class or have each handler expose its own tool definitions via a property:

```csharp
public interface IMcpToolHandler
{
    IReadOnlyList<string> SupportedTools { get; }
    JsonArray GetToolDefinitions(); // Each handler knows its own schemas
    Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct);
}
```

This keeps tool definitions co-located with their implementations.

## File Structure After Refactoring

```
AiCodeGraph.Cli/Mcp/
├── McpServer.cs              (~100 lines - protocol + dispatch)
├── IMcpToolHandler.cs        (~10 lines - interface)
├── McpToolDefinitions.cs     (optional: static tool defs if not in handlers)
├── McpProtocolHelpers.cs     (~50 lines - CreateResult, CreateError, CreateToolResult)
└── Handlers/
    ├── ContextHandler.cs     (~180 lines - cg_get_context with sub-methods)
    ├── AnalysisHandler.cs    (~250 lines - analyze, churn, coupling, diff, drift)
    ├── QueryHandler.cs       (~200 lines - hotspots, callgraph, tree, dead_code, impact)
    ├── SearchHandler.cs      (~150 lines - token_search, semantic_search, similar)
    └── DuplicatesHandler.cs  (~120 lines - duplicates, clusters, export_graph)
```

## Key Considerations

- Keep `VectorIndex` as a shared lazy singleton passed to SearchHandler since it's expensive to build
- The `AnalysisHandler` needs write access and the ability to invalidate the vector index cache
- Move `FormatAge`, `CountMethodsInNamespace`, `CountMethodsInType` to a shared `McpFormatHelpers` static class
- Move `CreateOpenAiEngineFromMetadata` and `CreateOnnxEngineFromMetadata` to `SearchHandler` since only `ToolSemanticSearch` uses them
- The `GetChangedCsFilesAsync` git helper moves to `AnalysisHandler`
- Preserve the existing MCP JSON-RPC protocol behavior exactly

**Test Strategy:**

1. **Build verification**: Run `dotnet build` after refactoring to ensure no compilation errors.

2. **Existing test suite**: Run `dotnet test` to verify all 178+ existing tests still pass (especially CliCommandTests and any MCP-related integration tests).

3. **Protocol fidelity tests**: Create `AiCodeGraph.Tests/McpHandlerTests.cs` with unit tests for each handler class:
   - Test ContextHandler returns all sections (metrics, callers, callees, cluster, duplicates, tests) using a mock StorageService
   - Test each handler returns proper error messages for missing required parameters
   - Test handler routing: verify each tool name maps to the correct handler

4. **ToolGetContext decomposition tests**: Verify the output of the refactored ContextHandler.HandleAsync matches the original output format exactly. Create a test with a known method in the fixture database and compare section-by-section output.

5. **Integration test**: Start the McpServer, send JSON-RPC messages through stdin, and verify responses match the pre-refactoring output format for: initialize, tools/list, and at least one tool call per handler category.

6. **Tool list completeness**: Verify `tools/list` response still contains all 17 tools with identical schemas (names, descriptions, inputSchema objects).

7. **Complexity verification**: After refactoring, run `ai-code-graph hotspots --db ./ai-code-graph/graph.db` and verify no method in the new handlers exceeds CC=15 (down from CC=59).

8. **Edge cases**: Test that the AnalysisHandler correctly invalidates the VectorIndex after re-analysis, and that SearchHandler correctly lazy-initializes the VectorIndex on first search call.
