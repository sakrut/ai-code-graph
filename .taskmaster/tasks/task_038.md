# Task ID: 38

**Title:** Dead-Code Command - Unreachable Method Detection

**Status:** done

**Dependencies:** 20 ✓

**Priority:** high

**Description:** Create a new 'dead-code' CLI command that identifies methods with zero callers, excluding public API methods, test methods, Main entry points, and interface implementations.

**Details:**

File: AiCodeGraph.Cli/Program.cs (new command registration)

```csharp
var deadCodeCommand = new Command("dead-code", "Find methods with no callers (potential dead code)");
var deadCodeDbOption = new Option<string>("--db", () => "./ai-code-graph/graph.db", "Database path");
var deadCodeFormatOption = new Option<string>("--format", () => "table", "Output format: table|json");
var deadCodeIncludePublicOption = new Option<bool>("--include-public", () => false, "Include public methods");
deadCodeCommand.AddOption(deadCodeDbOption);
deadCodeCommand.AddOption(deadCodeFormatOption);
deadCodeCommand.AddOption(deadCodeIncludePublicOption);

deadCodeCommand.SetAction(async (parseResult, ct) =>
{
    var dbPath = parseResult.GetValue(deadCodeDbOption)!;
    var format = parseResult.GetValue(deadCodeFormatOption)!;
    var includePublic = parseResult.GetValue(deadCodeIncludePublicOption);
    
    using var storage = new StorageService(dbPath);
    await storage.OpenAsync(ct);
    
    // SQL query for methods with no callers
    // Need to add a new method to StorageService or use raw query
    // Exclude: test methods, Main, interface implementations
    // The SQL approach from PRD:
    // SELECT m.* FROM Methods m LEFT JOIN MethodCalls mc ON m.Id = mc.CalleeId WHERE mc.CallerId IS NULL
    
    // Filter out:
    // - Methods in *Tests* namespaces/types
    // - Methods named Main, .ctor (constructors)
    // - Interface method implementations (check TypeImplements table)
    // - Public methods (unless --include-public)
    
    // Output table: Method name, File, LOC, Complexity
});
rootCommand.AddCommand(deadCodeCommand);
```

Add a new method to StorageService (or IStorageService):
```csharp
public async Task<List<(string Id, string Name, string FullName, string? FilePath, int StartLine, int? CC, int? LOC)>> GetDeadCodeAsync(bool includePublic, CancellationToken ct)
```

Also add MCP tool `cg_dead_code` in McpServer.cs and slash command `.claude/commands/cg:dead-code.md`.

**Test Strategy:**

Add dead-code methods to test fixture (private methods never called). Verify they're detected. Verify public API methods are excluded by default. Verify test methods are excluded. Verify --include-public flag includes them. Test JSON output format. Test empty result (all methods have callers).

## Subtasks

### 38.1. Add GetDeadCodeAsync to StorageService with LEFT JOIN query

**Status:** pending  
**Dependencies:** None  

Implement a new async method in StorageService that queries for methods with zero incoming callers using a LEFT JOIN on MethodCalls, excluding test methods (namespaces/types containing 'Test'), constructors (.ctor, .cctor), and Main entry points.

**Details:**

Add `GetDeadCodeAsync(bool includePublic, CancellationToken ct)` to StorageService.cs returning a list of tuples with Id, Name, FullName, FilePath, StartLine, CognitiveComplexity, and LinesOfCode. The SQL query should LEFT JOIN Methods with MethodCalls on m.Id = mc.CalleeId, LEFT JOIN Metrics for CC/LOC data, and filter WHERE mc.CallerId IS NULL. Add exclusion conditions: m.Name NOT IN ('.ctor', '.cctor', 'Main'), m.FullName NOT LIKE '%Test%', and m.IsAbstract = 0. When includePublic is false, also exclude methods where the accessibility is public (check if Methods table has accessibility info, or filter by naming convention). Return results ordered by CognitiveComplexity DESC.

### 38.2. Register dead-code CLI command in Program.cs with options

**Status:** pending  
**Dependencies:** 38.1  

Add the dead-code command to Program.cs with --db, --format (table|json), and --include-public options, implementing the action handler that calls GetDeadCodeAsync and formats output.

**Details:**

In Program.cs, create the dead-code command following the existing command registration pattern: define Option<string> for --db (default './ai-code-graph/graph.db'), Option<string> for --format (default 'table'), and Option<bool> for --include-public (default false). In SetAction, validate the database file exists (exit code 1 if not), open StorageService with OpenAsync, call GetDeadCodeAsync, and format results. For table format, output columns: Method, File, Line, CC, LOC with Console.WriteLine. For JSON format, serialize to JSON array with System.Text.Json. Register command with rootCommand.AddCommand(deadCodeCommand).

### 38.3. Implement interface implementation filtering logic

**Status:** pending  
**Dependencies:** 38.1  

Add filtering to exclude methods that are interface implementations by checking the database schema for type-implements relationships or using FullName/naming conventions to identify interface members.

**Details:**

Examine the SchemaDefinition for any TypeImplements or InterfaceImplementations table. If such a table exists, add a NOT EXISTS subquery in GetDeadCodeAsync to exclude methods whose TypeId implements an interface and whose Name matches an interface method. If no such table exists, use heuristic filtering: check if the method's type has IsAbstract markers or if the FullName pattern matches known interface implementation patterns. Additionally, exclude methods marked IsVirtual = 1 or IsAbstract = 1 as these are meant for polymorphic dispatch and may be called dynamically. Add an IsOverride check if the column exists. Update the SQL query or add post-query filtering in C# for cases that can't be handled in SQL alone.

### 38.4. Add MCP tool cg_dead_code, slash command, and tests

**Status:** pending  
**Dependencies:** 38.1, 38.2, 38.3  

Register a new cg_dead_code MCP tool in McpServer.cs, create the .claude/commands/cg:dead-code.md slash command file, and add comprehensive xUnit tests covering the dead-code detection feature.

**Details:**

In McpServer.cs: add tool definition in HandleToolsList with name 'cg_dead_code', description 'Find methods with no callers (potential dead code)', and properties for include_public (boolean, default false) and top (integer, default 20). Add case in HandleToolCall switch to invoke a ToolGetDeadCode method that opens storage, calls GetDeadCodeAsync, and formats results as newline-separated strings with method name, file, and complexity. Create .claude/commands/cg:dead-code.md following existing slash command patterns with steps to run the CLI command. Create DeadCodeTests.cs in AiCodeGraph.Tests with test fixture containing known dead-code methods (private unused methods) and known live methods (called methods), testing all exclusion rules and both output formats.
