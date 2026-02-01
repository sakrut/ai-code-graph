# Task ID: 8

**Title:** Implement CLI Query Commands: callgraph, hotspots, tree

**Status:** done

**Dependencies:** 4 ✓, 7 ✓

**Priority:** medium

**Description:** Add CLI commands for querying the stored code graph: exploring call graphs with depth control, finding complexity hotspots, and displaying the code tree structure.

**Details:**

1. **`callgraph` command:**
   ```csharp
   var callgraphCmd = new Command("callgraph", "Explore method call graph");
   callgraphCmd.AddArgument(new Argument<string>("method", "Method name or pattern"));
   callgraphCmd.AddOption(new Option<int>("--depth", () => 2, "Traversal depth"));
   callgraphCmd.AddOption(new Option<string>("--direction", () => "both", "callers|callees|both"));
   callgraphCmd.AddOption(new Option<string>("--format", () => "tree", "tree|json"));
   ```
   - Resolve method by name (support partial matching, namespace-qualified)
   - BFS/DFS traversal to specified depth
   - Tree output: indented with arrows showing direction
   - JSON output: nodes + edges format

2. **`hotspots` command:**
   ```csharp
   var hotspotsCmd = new Command("hotspots", "Show complexity hotspots");
   hotspotsCmd.AddOption(new Option<int>("--top", () => 20, "Number of results"));
   hotspotsCmd.AddOption(new Option<int>("--threshold", "Minimum complexity"));
   hotspotsCmd.AddOption(new Option<string>("--format", () => "table", "table|json"));
   ```
   - Query methods ordered by cognitive complexity DESC
   - Display: method name, complexity, LOC, nesting depth, file:line

3. **`tree` command:**
   ```csharp
   var treeCmd = new Command("tree", "Display code structure tree");
   treeCmd.AddOption(new Option<string>("--namespace", "Filter by namespace"));
   treeCmd.AddOption(new Option<string>("--type", "Filter by type name"));
   treeCmd.AddOption(new Option<string>("--format", () => "tree", "tree|json"));
   ```
   - Show: Project → Namespace → Type → Methods
   - Support filtering by namespace or type

4. All commands support `--format json` for machine consumption
5. All JSON output is deterministic (sorted keys, consistent ordering)

**Test Strategy:**

Integration tests against a pre-analyzed test database. Test callgraph: verify correct traversal depth, direction filtering, partial name matching. Test hotspots: verify ordering, threshold filtering, correct metrics display. Test tree: verify hierarchy, namespace filtering. Test JSON output is valid and deterministic. Test edge cases: method not found, empty results.

## Subtasks

### 8.1. Implement callgraph command with method resolution and partial name matching

**Status:** done  
**Dependencies:** None  

Create the callgraph CLI command with argument parsing, method name resolution supporting partial matching and namespace-qualified names, and wire up the command to System.CommandLine.

**Details:**

1. Define the `callgraph` command using System.CommandLine:
   - Add `method` argument (string, required) for method name or pattern
   - Add `--depth` option (int, default 2) for traversal depth
   - Add `--direction` option (string, default 'both') accepting 'callers', 'callees', or 'both'
   - Add `--format` option (string, default 'tree') accepting 'tree' or 'json'
2. Implement `MethodResolver` class that:
   - Queries the database for methods matching the input pattern
   - Supports exact match by full qualified name (e.g., 'Namespace.Class.Method')
   - Supports partial matching by method name only (e.g., 'DoWork' matches 'MyApp.Service.DoWork')
   - Supports wildcard/glob patterns (e.g., '*Repository.Get*')
   - Returns disambiguation list if multiple matches found, prompting user to be more specific
3. Register the command with the root command in Program.cs
4. Handle error cases: no matches found, ambiguous matches, invalid direction values

### 8.2. Implement BFS/DFS graph traversal with depth control and direction filtering

**Status:** done  
**Dependencies:** 8.1  

Implement the core graph traversal logic for the callgraph command, supporting BFS traversal with configurable depth limits and directional filtering (callers, callees, or both).

**Details:**

1. Create `CallGraphTraverser` class with:
   - `TraverseAsync(string methodId, int depth, TraversalDirection direction)` method
   - BFS implementation using a queue with depth tracking
   - Visited set to avoid cycles in the graph
2. Implement direction-based traversal:
   - `callers`: Follow incoming edges (who calls this method) - query CallRelationships where CalleeId matches
   - `callees`: Follow outgoing edges (what does this method call) - query CallRelationships where CallerId matches
   - `both`: Traverse in both directions, marking edge direction in results
3. Depth control:
   - Track current depth during BFS
   - Stop expanding nodes beyond specified depth
   - Include depth level in result nodes for rendering
4. Return a `CallGraphResult` containing:
   - Root node (the queried method)
   - List of nodes with depth levels and method metadata
   - List of edges with direction indicators
5. Query the SQLite database for call relationships at each traversal step
6. Handle edge cases: methods with no callers/callees, self-recursive methods, very deep graphs

### 8.3. Implement hotspots command with complexity-ordered queries and threshold filtering

**Status:** done  
**Dependencies:** None  

Create the hotspots CLI command that queries methods ordered by cognitive complexity, supports threshold filtering and top-N limiting, and displays method name, complexity score, LOC, nesting depth, and file location.

**Details:**

1. Define the `hotspots` command using System.CommandLine:
   - Add `--top` option (int, default 20) for number of results
   - Add `--threshold` option (int, optional) for minimum complexity score
   - Add `--format` option (string, default 'table') accepting 'table' or 'json'
2. Implement `HotspotsQuery` class that:
   - Queries the database for methods with complexity metrics
   - Orders results by cognitive complexity DESC
   - Applies threshold filter: WHERE complexity >= threshold (if specified)
   - Limits results to top N
3. Result model includes for each method:
   - Full qualified method name
   - Cognitive complexity score
   - Lines of code (LOC)
   - Maximum nesting depth
   - File path and line number (file:line format)
4. Register command with root command in Program.cs
5. Handle edge cases: no methods above threshold, empty database, methods without complexity data

### 8.4. Implement tree command with namespace/type hierarchy display and filtering

**Status:** done  
**Dependencies:** None  

Create the tree CLI command that displays the code structure as a hierarchy (Project → Namespace → Type → Methods) with support for namespace and type name filtering.

**Details:**

1. Define the `tree` command using System.CommandLine:
   - Add `--namespace` option (string, optional) to filter by namespace prefix
   - Add `--type` option (string, optional) to filter by type name
   - Add `--format` option (string, default 'tree') accepting 'tree' or 'json'
2. Implement `TreeQuery` class that:
   - Queries database for the full project → namespace → type → method hierarchy
   - Applies namespace filter: WHERE namespace LIKE 'filter%' (prefix match)
   - Applies type filter: WHERE type_name LIKE '%filter%' (contains match)
   - Builds a hierarchical result model
3. Tree result model:
   - `ProjectNode` containing list of `NamespaceNode`
   - `NamespaceNode` containing list of `TypeNode`
   - `TypeNode` containing type kind (class/interface/record/struct) and list of `MethodNode`
   - `MethodNode` with name, return type, parameter count
4. Tree rendering uses box-drawing characters:
   - `├──` for intermediate items, `└──` for last items
   - Different prefixes/icons for different node types
5. Register command with root command
6. Handle: empty namespaces after filtering, types with no methods

### 8.5. Implement table formatting with proper column alignment for all commands

**Status:** done  
**Dependencies:** 8.1, 8.2, 8.3, 8.4  

Create a shared table formatter that renders human-readable table output with proper column alignment, truncation for long values, and consistent styling across all query commands.

**Details:**

1. Create `TableFormatter` utility class:
   - Accept column definitions: name, alignment (left/right), max width
   - Calculate column widths based on content (auto-sizing)
   - Support minimum and maximum column widths
   - Truncate long values with ellipsis ('...')
2. Implement tree/indented output for callgraph:
   - Use arrow indicators: `→` for callees, `←` for callers
   - Indent by depth level (2-4 spaces per level)
   - Show method signature and file location at each node
   - Example: `  → ServiceClass.ProcessData() [src/Service.cs:45]`
3. Implement table output for hotspots:
   - Columns: Method, Complexity, LOC, MaxNesting, Location
   - Right-align numeric columns
   - Header row with separator line
   - Example: `MyClass.ComplexMethod    42    150    8    src/MyClass.cs:23`
4. Implement tree output for tree command:
   - Box-drawing characters for hierarchy (├──, └──, │)
   - Type annotations: [C] class, [I] interface, [R] record, [S] struct
   - Method signatures with return types
5. Add color/ANSI support (optional, respect NO_COLOR env var)
6. Ensure consistent formatting across all commands

### 8.6. Implement JSON output format for all commands with deterministic sorted output

**Status:** done  
**Dependencies:** 8.1, 8.2, 8.3, 8.4  

Add JSON output mode to all query commands ensuring deterministic output with sorted keys, consistent ordering of arrays, and a stable schema suitable for machine consumption and piping to other tools.

**Details:**

1. Create `JsonOutputFormatter` utility class:
   - Use System.Text.Json with `JsonSerializerOptions` configured for:
     - `WriteIndented = true` for readable output
     - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
     - Custom converter for deterministic key ordering
   - Ensure all dictionary/object keys are sorted alphabetically
   - Ensure all arrays have stable ordering (by ID or name)
2. Define JSON schemas for each command:
   - **callgraph**: `{ "root": { "id", "name", "file" }, "nodes": [...], "edges": [{ "from", "to", "direction" }], "metadata": { "depth", "direction" } }`
   - **hotspots**: `{ "hotspots": [{ "method", "complexity", "loc", "maxNesting", "location" }], "metadata": { "total", "threshold", "top" } }`
   - **tree**: `{ "projects": [{ "name", "namespaces": [{ "name", "types": [{ "name", "kind", "methods": [...] }] }] }] }`
3. Include metadata in all outputs:
   - Query parameters used
   - Timestamp of query
   - Total count of results
4. Implement deterministic ordering:
   - Nodes sorted by full qualified name
   - Edges sorted by (from, to)
   - Hotspots sorted by complexity DESC, then name ASC
   - Tree items sorted alphabetically within each level
5. Wire `--format json` option to use JsonOutputFormatter in each command handler
6. Validate output against schema in tests
