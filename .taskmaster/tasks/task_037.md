# Task ID: 37

**Title:** Impact Command - Transitive Callers Analysis

**Status:** done

**Dependencies:** 20 ✓

**Priority:** high

**Description:** Create a new 'impact' CLI command that shows the full transitive caller chain for a method using BFS traversal, with tree and JSON output formats.

**Details:**

File: AiCodeGraph.Cli/Program.cs (new command registration)

Register new command:
```csharp
var impactCommand = new Command("impact", "Show transitive impact of changing a method");
var impactMethodOption = new Option<string>("--method", "Method ID or partial name") { IsRequired = true };
var impactDepthOption = new Option<int?>("--depth", "Max traversal depth (default: unlimited)");
var impactFormatOption = new Option<string>("--format", () => "tree", "Output format: tree|json");
var impactDbOption = new Option<string>("--db", () => "./ai-code-graph/graph.db", "Database path");
impactCommand.AddOption(impactMethodOption);
impactCommand.AddOption(impactDepthOption);
impactCommand.AddOption(impactFormatOption);
impactCommand.AddOption(impactDbOption);

impactCommand.SetAction(async (parseResult, ct) =>
{
    var method = parseResult.GetValue(impactMethodOption)!;
    var maxDepth = parseResult.GetValue(impactDepthOption);
    var format = parseResult.GetValue(impactFormatOption)!;
    var dbPath = parseResult.GetValue(impactDbOption)!;
    
    using var storage = new StorageService(dbPath);
    await storage.OpenAsync(ct);
    
    // Resolve method ID (partial match)
    var matches = await storage.SearchMethodsAsync($"%{method}%", ct);
    if (matches.Count == 0) { /* error */ return 1; }
    var targetId = matches[0].Item1;
    
    // BFS for callers
    var visited = new HashSet<string>();
    var queue = new Queue<(string Id, int Depth)>();
    var tree = new Dictionary<string, List<string>>(); // child -> parents
    var entryPoints = new List<string>();
    
    queue.Enqueue((targetId, 0));
    visited.Add(targetId);
    
    while (queue.Count > 0)
    {
        var (current, depth) = queue.Dequeue();
        if (maxDepth.HasValue && depth >= maxDepth.Value) continue;
        
        var callers = await storage.GetCallersAsync(current, ct);
        if (callers.Count == 0 && current != targetId)
            entryPoints.Add(current);
        
        foreach (var caller in callers)
        {
            if (visited.Add(caller))
            {
                tree.TryAdd(caller, new List<string>());
                tree[caller].Add(current);
                queue.Enqueue((caller, depth + 1));
            }
        }
    }
    
    // Output
    if (format == "json") { /* JSON output */ }
    else { /* Tree output with indentation */ }
    
    Console.WriteLine($"Total: {visited.Count} methods affected, {entryPoints.Count} entry points");
    return 0;
});
rootCommand.AddCommand(impactCommand);
```

Also add MCP tool `cg_impact` in McpServer.cs and slash command `.claude/commands/cg:impact.md`.

**Test Strategy:**

Test with fixture methods that have known caller chains. Verify BFS finds all transitive callers. Test --depth limit cuts off at correct level. Verify entry points are correctly identified (methods with no callers). Test JSON output format. Test with method that has no callers. Test with circular call references (BFS visited set prevents infinite loop).

## Subtasks

### 37.1. Register impact command with options in Program.cs and implement method resolution

**Status:** pending  
**Dependencies:** None  

Add the 'impact' command to Program.cs with --method (required), --depth, --format, and --db options following the existing System.CommandLine 2.0.2 pattern. Implement method resolution using StorageService.SearchMethodsAsync for partial name matching, with proper error handling for no matches and multiple matches.

**Details:**

Create the impact command registration block in Program.cs following the established pattern:
1. Define options: impactMethodOption (Option<string>, required), impactDepthOption (Option<int?>), impactFormatOption (Option<string>, default 'tree'), impactDbOption (Option<string>, default './ai-code-graph/graph.db')
2. Create Command('impact', 'Show transitive impact of changing a method') and add all options
3. In SetAction handler: validate database exists (File.Exists check), open StorageService with OpenAsync, call SearchMethodsAsync with wildcard pattern for partial matching
4. Handle edge cases: no matches found (error message + exit code 1), multiple matches (use first match or list disambiguation)
5. Add command to rootCommand. Follow existing exit code conventions (0=success, 1=expected error, 2=unexpected error)

### 37.2. Implement BFS traversal for transitive callers with depth limiting

**Status:** pending  
**Dependencies:** 37.1  

Implement the core BFS (Breadth-First Search) algorithm within the impact command's SetAction handler that traverses the call graph upward through callers, tracking visited nodes, depth levels, parent-child relationships, and identifying entry points (methods with no callers).

**Details:**

Implement BFS traversal logic after method resolution:
1. Initialize data structures: HashSet<string> visited, Queue<(string Id, int Depth)> queue, Dictionary<string, List<string>> tree (maps each node to its callees in the traversal), List<string> entryPoints
2. Seed queue with resolved target method at depth 0, add to visited
3. BFS loop: dequeue current node, skip if maxDepth reached, call storage.GetCallersAsync(current) to get callers
4. For each caller not in visited: add to visited, record parent-child relationship in tree dict, enqueue at depth+1
5. Track entry points: methods that have no callers themselves (leaf nodes in upward traversal, excluding the target)
6. Handle edge cases: method with no callers at all (only the target itself), circular references (handled by visited set), very deep graphs with --depth limiting

### 37.3. Implement tree and JSON output formatters with entry point identification

**Status:** pending  
**Dependencies:** 37.2  

Create two output formatters for the impact command results: a tree format with indentation showing the caller hierarchy, and a JSON format with structured data including affected methods, entry points, and depth information.

**Details:**

Implement output formatting after BFS traversal completes:
1. Tree format (default): Build indented tree representation starting from target method, showing callers at each level with indent characters (e.g., Unicode box-drawing or simple spaces/pipes). Use recursive rendering from target upward through the tree dictionary. Show method names (shortened from FullName) with depth indicators.
2. JSON format: Serialize to JSON with JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }. Include: target method, total affected count, entry points list, full traversal tree with depth annotations.
3. Summary line for both formats: 'Total: {visited.Count} methods affected, {entryPoints.Count} entry points'
4. Use GetMethodInfoAsync to resolve method names from IDs for display. Handle cases where method info might not be found (show raw ID as fallback).

### 37.4. Add MCP tool cg_impact, slash command, and integration tests

**Status:** pending  
**Dependencies:** 37.1, 37.2, 37.3  

Register a new cg_impact MCP tool in McpServer.cs following the existing pattern, create the .claude/commands/cg:impact.md slash command file, and write comprehensive integration tests covering the BFS traversal, depth limiting, output formats, and edge cases.

**Details:**

Three deliverables:
1. MCP Tool (McpServer.cs): Add 'cg_impact' to HandleToolsList with parameters: method (string, required), depth (integer, optional), format (string, optional, default 'tree'). Add case in HandleToolCall switch. Implement tool method that opens storage, resolves method, runs BFS, returns formatted string output.
2. Slash Command (.claude/commands/cg:impact.md): Create markdown file with description 'Show transitive impact of changing a method: $ARGUMENTS', steps for running ai-code-graph impact command, guidance on interpreting results (affected count, entry points, tree depth).
3. Tests (AiCodeGraph.Tests/): Create ImpactCommandTests.cs with in-memory StorageService. Seed database with known call chains (A calls B calls C, D calls B, E calls A). Test cases: full traversal finds all transitive callers, --depth=1 only finds direct callers, method with no callers returns only itself, JSON output is valid and contains expected fields, entry points correctly identified, partial method name matching works.
