# Task ID: 63

**Title:** Refactor CLI Program.cs - Split Monolithic File into Command Handler Classes

**Status:** done

**Dependencies:** 52 ✓, 8 ✓, 25 ✓

**Priority:** high

**Description:** Split the 2371-line monolithic Program.cs into separate command handler classes following the existing MCP handler pattern. Create a Commands/ folder with ICommandHandler interface and individual command files while extracting helper functions to a Helpers/ folder.

**Details:**

## Current State Analysis

`AiCodeGraph.Cli/Program.cs` is a 2371-line monolithic file containing:
- 19 CLI commands defined inline (analyze, callgraph, hotspots, tree, similar, duplicates, clusters, token-search, semantic-search, export, drift, context, impact, dead-code, churn, coupling, diff, mcp, setup-claude)
- Helper functions and static utilities (lines 2037-2371)
- VectorIndexCache static class
- All command registration with rootCommand

## Pattern to Follow

Follow the successful MCP refactoring pattern from Task 52:
- `AiCodeGraph.Cli/Mcp/IMcpToolHandler.cs` - Interface definition
- `AiCodeGraph.Cli/Mcp/Handlers/*.cs` - Individual handler classes
- `AiCodeGraph.Cli/Mcp/McpProtocolHelpers.cs` - Shared helpers

## Implementation Steps

### Step 1: Create ICommandHandler Interface

Create `AiCodeGraph.Cli/Commands/ICommandHandler.cs`:
```csharp
using System.CommandLine;

namespace AiCodeGraph.Cli.Commands;

public interface ICommandHandler
{
    Command BuildCommand();
}
```

### Step 2: Extract Helper Functions to Helpers Folder

Create `AiCodeGraph.Cli/Helpers/` directory with:

**ModelCountHelpers.cs** (lines 2044-2075):
```csharp
namespace AiCodeGraph.Cli.Helpers;

public static class ModelCountHelpers
{
    public static int CountTypes(ProjectModel project) { ... }
    public static int CountTypesInNamespace(NamespaceModel ns) { ... }
    public static int CountNestedTypes(TypeModel type) { ... }
    public static int CountMethods(ProjectModel project) { ... }
    public static int CountMethodsInNamespace(NamespaceModel ns) { ... }
    public static int CountMethodsInType(TypeModel type) { ... }
}
```

**OutputHelpers.cs** (lines 2077-2109, 2311-2318):
```csharp
namespace AiCodeGraph.Cli.Helpers;

public static class OutputHelpers
{
    public static string CsvEscape(string value) { ... }
    public static void PrintCallTree(...) { ... }
    public static string FormatAge(TimeSpan age) { ... }
}
```

**AnalysisStageHelpers.cs** (lines 2112-2199, 2229-2309):
```csharp
namespace AiCodeGraph.Cli.Helpers;

public static class AnalysisStageHelpers
{
    public static async Task<LoadedWorkspace> LoadWorkspaceStage(...) { ... }
    public static List<ExtractionResult> ExtractCodeModelStage(...) { ... }
    public static List<MethodCallEdge> BuildCallGraphStage(...) { ... }
    public static List<MethodMetrics> ComputeMetricsStage(...) { ... }
    public static List<NormalizedMethod> NormalizeMethodsStage(...) { ... }
    public static List<...> GenerateEmbeddingsStage(...) { ... }
    public static IEmbeddingEngine CreateEmbeddingEngine(...) { ... }
    public static async Task StoreResultsStage(...) { ... }
    public static async Task<...> DetectDuplicatesStage(...) { ... }
    public static void SaveBaselineStage(...) { ... }
    public static void PrintAnalysisSummary(...) { ... }
}
```

**GitHelpers.cs** (lines 2320-2341):
```csharp
namespace AiCodeGraph.Cli.Helpers;

public static class GitHelpers
{
    public static async Task<List<string>> GetChangedCsFiles(...) { ... }
}
```

**VectorIndexCache.cs** (lines 2343-2371):
```csharp
namespace AiCodeGraph.Cli.Helpers;

public static class VectorIndexCache { ... }
```

### Step 3: Create Individual Command Handler Classes

Create `AiCodeGraph.Cli/Commands/` directory with these files:

1. **AnalyzeCommand.cs** - analyze command (lines 43-113)
2. **CallgraphCommand.cs** - callgraph command (lines 115-221)
3. **HotspotsCommand.cs** - hotspots command (lines 223-287)
4. **TreeCommand.cs** - tree command (lines 289-389)
5. **SimilarCommand.cs** - similar command (lines 391-472)
6. **DuplicatesCommand.cs** - duplicates command (lines 474-559)
7. **ClustersCommand.cs** - clusters command (lines 561-623)
8. **TokenSearchCommand.cs** - token-search command (lines 625-716)
9. **SemanticSearchCommand.cs** - semantic-search command (lines 718-813)
10. **ExportCommand.cs** - export command (lines 815-886)
11. **DriftCommand.cs** - drift command (lines 888-1029)
12. **ContextCommand.cs** - context command (lines 1031-1273)
13. **ImpactCommand.cs** - impact command (lines 1275-1411)
14. **DeadCodeCommand.cs** - dead-code command (lines 1413-1475)
15. **ChurnCommand.cs** - churn command (lines 1477-1545)
16. **CouplingCommand.cs** - coupling command (lines 1547-1615)
17. **DiffCommand.cs** - diff command (lines 1617-1717)
18. **McpCommand.cs** - mcp command (lines 1737-1749)
19. **SetupClaudeCommand.cs** - setup-claude command (lines 1751-2032)

Each command handler follows this pattern:
```csharp
using System.CommandLine;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class HotspotsCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var topOption = new Option<int>("--top", "-t") { ... };
        var thresholdOption = new Option<int?>("--threshold") { ... };
        var formatOption = new Option<string>("--format", "-f") { ... };
        var dbOption = new Option<string>("--db") { ... };

        var command = new Command("hotspots", "Show complexity hotspots")
        {
            topOption, thresholdOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, ct) =>
        {
            // Existing handler logic
        });

        return command;
    }
}
```

### Step 4: Create CommandRegistry

Create `AiCodeGraph.Cli/Commands/CommandRegistry.cs`:
```csharp
using System.CommandLine;

namespace AiCodeGraph.Cli.Commands;

public static class CommandRegistry
{
    public static RootCommand Build()
    {
        var rootCommand = new RootCommand("AI Code Graph - Semantic code analysis for .NET");

        var handlers = new ICommandHandler[]
        {
            new AnalyzeCommand(),
            new CallgraphCommand(),
            new HotspotsCommand(),
            new TreeCommand(),
            new SimilarCommand(),
            new DuplicatesCommand(),
            new ClustersCommand(),
            new TokenSearchCommand(),
            new SemanticSearchCommand(),
            new ExportCommand(),
            new DriftCommand(),
            new ContextCommand(),
            new ImpactCommand(),
            new DeadCodeCommand(),
            new ChurnCommand(),
            new CouplingCommand(),
            new DiffCommand(),
            new McpCommand(),
            new SetupClaudeCommand()
        };

        foreach (var handler in handlers)
        {
            rootCommand.Add(handler.BuildCommand());
        }

        return rootCommand;
    }
}
```

### Step 5: Reduce Program.cs to Entry Point

Final `Program.cs` (~15 lines):
```csharp
using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Cli.Commands;

var rootCommand = CommandRegistry.Build();
var parseResult = CommandLineParser.Parse(rootCommand, args);
return await parseResult.InvokeAsync();
```

## File Structure After Refactoring

```
AiCodeGraph.Cli/
├── Program.cs              (~15 lines - entry point only)
├── Commands/
│   ├── ICommandHandler.cs
│   ├── CommandRegistry.cs
│   ├── AnalyzeCommand.cs
│   ├── CallgraphCommand.cs
│   ├── HotspotsCommand.cs
│   ├── TreeCommand.cs
│   ├── SimilarCommand.cs
│   ├── DuplicatesCommand.cs
│   ├── ClustersCommand.cs
│   ├── TokenSearchCommand.cs
│   ├── SemanticSearchCommand.cs
│   ├── ExportCommand.cs
│   ├── DriftCommand.cs
│   ├── ContextCommand.cs
│   ├── ImpactCommand.cs
│   ├── DeadCodeCommand.cs
│   ├── ChurnCommand.cs
│   ├── CouplingCommand.cs
│   ├── DiffCommand.cs
│   ├── McpCommand.cs
│   └── SetupClaudeCommand.cs
├── Helpers/
│   ├── ModelCountHelpers.cs
│   ├── OutputHelpers.cs
│   ├── AnalysisStageHelpers.cs
│   ├── GitHelpers.cs
│   └── VectorIndexCache.cs
└── Mcp/
    └── (existing MCP handlers)
```

## Critical Constraints

1. **No CLI interface changes** - All command names, arguments, options, and default values must remain identical
2. **Preserve exact behavior** - Each command's output format and error handling must be unchanged
3. **Maintain exit codes** - Commands must return same exit codes for success/failure cases
4. **Keep static helpers static** - Functions like FormatAge, CsvEscape that are pure functions remain static

## Shared Dependencies

Commands that share common patterns should use shared helpers:
- Database path validation and opening (many commands)
- JSON serialization options (reuse across all commands)
- Error handling for missing database (centralize in helper)

**Test Strategy:**

## Build Verification

1. **Compile check**: Run `dotnet build AiCodeGraph.Cli` - must complete with no errors
2. **Warning check**: Build output should not introduce new warnings

## CLI Interface Verification

For each of the 19 commands, verify the help text matches exactly:

```bash
# Generate help text before refactoring (save as baseline)
ai-code-graph --help > before/root-help.txt
ai-code-graph analyze --help > before/analyze-help.txt
ai-code-graph callgraph --help > before/callgraph-help.txt
# ... repeat for all commands

# Generate help text after refactoring
ai-code-graph --help > after/root-help.txt
ai-code-graph analyze --help > after/analyze-help.txt
# ... repeat for all commands

# Diff all help files - must be identical
diff -r before/ after/
```

## Functional Testing

1. **Run full test suite**: `dotnet test` - all 303+ existing tests must pass
2. **CLI integration tests**: Existing CliCommandTests must pass unchanged

## Manual Smoke Tests

Execute each command against test solution to verify identical output:

```bash
# Analyze
ai-code-graph analyze tests/fixtures/TestSolution/TestSolution.sln -o ./test-output

# Query commands
ai-code-graph hotspots --top 5 --db ./test-output/graph.db
ai-code-graph tree --db ./test-output/graph.db
ai-code-graph callgraph "TestMethod" --db ./test-output/graph.db
ai-code-graph similar "TestMethod" --db ./test-output/graph.db
ai-code-graph duplicates --db ./test-output/graph.db
ai-code-graph clusters --db ./test-output/graph.db
ai-code-graph token-search "test" --db ./test-output/graph.db
ai-code-graph semantic-search "test" --db ./test-output/graph.db
ai-code-graph context "TestMethod" --db ./test-output/graph.db
ai-code-graph impact "TestMethod" --db ./test-output/graph.db
ai-code-graph dead-code --db ./test-output/graph.db
ai-code-graph churn --db ./test-output/graph.db
ai-code-graph coupling --db ./test-output/graph.db
ai-code-graph diff --db ./test-output/graph.db
ai-code-graph export --db ./test-output/graph.db

# Drift (requires baseline)
ai-code-graph analyze tests/fixtures/TestSolution/TestSolution.sln -o ./test-output --save-baseline
ai-code-graph drift --db ./test-output/graph.db

# MCP server (verify starts without error)
echo '{"jsonrpc":"2.0","id":1,"method":"initialize"}' | ai-code-graph mcp --db ./test-output/graph.db

# Setup command
mkdir -p /tmp/test-setup && cd /tmp/test-setup && ai-code-graph setup-claude
```

## JSON Output Format Verification

For commands with `--format json` option, verify JSON structure is identical:

```bash
ai-code-graph hotspots --format json --db ./test-output/graph.db > before.json
# After refactor
ai-code-graph hotspots --format json --db ./test-output/graph.db > after.json
diff before.json after.json
```

## Exit Code Verification

```bash
# Success case (exit 0)
ai-code-graph hotspots --db ./test-output/graph.db; echo $?

# Missing database (exit 1)
ai-code-graph hotspots --db ./nonexistent.db; echo $?

# Invalid method pattern (exit 1)
ai-code-graph callgraph "NonexistentMethod" --db ./test-output/graph.db; echo $?
```

## Code Quality

1. Verify each command handler is self-contained
2. Verify no code duplication between command handlers (use shared helpers)
3. Verify helper classes are properly organized by responsibility
4. Verify all `using` statements are correct and minimal

## Subtasks

### 63.1. Create ICommandHandler Interface and CommandRegistry Infrastructure

**Status:** pending  
**Dependencies:** None  

Establish the foundation for the refactoring by creating the ICommandHandler interface and CommandRegistry class in a new Commands/ folder, following the pattern from the MCP handlers refactoring.

**Details:**

Create `AiCodeGraph.Cli/Commands/ICommandHandler.cs` with a single method `Command BuildCommand()` that returns a System.CommandLine.Command instance. Create `AiCodeGraph.Cli/Commands/CommandRegistry.cs` with a static `Build()` method that instantiates all command handlers and adds them to a RootCommand. The registry should maintain the same command registration order as the current Program.cs (analyze, callgraph, hotspots, tree, similar, duplicates, clusters, token-search, semantic-search, export, drift, context, impact, dead-code, churn, coupling, diff, mcp, setup-claude). Include proper using statements for System.CommandLine and the namespace AiCodeGraph.Cli.Commands. Verify the infrastructure compiles with `dotnet build AiCodeGraph.Cli`.

### 63.2. Extract Helper Functions to Helpers/ Folder

**Status:** pending  
**Dependencies:** 63.1  

Extract all static helper functions and utility classes from Program.cs lines 2148-2482 into dedicated helper classes within a new Helpers/ folder.

**Details:**

Create `AiCodeGraph.Cli/Helpers/` directory. Extract to `ModelCountHelpers.cs`: CountTypes, CountTypesInNamespace, CountNestedTypes, CountMethods, CountMethodsInNamespace, CountMethodsInType (lines 2155-2186). Extract to `OutputHelpers.cs`: CsvEscape, PrintCallTree, FormatAge, HandleCommandError (lines 2148-2153, 2188-2221, 2422-2429). Extract to `AnalysisStageHelpers.cs`: LoadWorkspaceStage, ExtractCodeModelStage, BuildCallGraphStage, ComputeMetricsStage, NormalizeMethodsStage, GenerateEmbeddingsStage, CreateEmbeddingEngine, StoreResultsStage, DetectDuplicatesStage, SaveBaselineStage, PrintAnalysisSummary (lines 2223-2420). Extract to `GitHelpers.cs`: GetChangedCsFiles (lines 2431-2452). Extract to `VectorIndexCache.cs`: the entire static class (lines 2454-2482). All helpers should be public static and use proper namespaces with required using statements.

### 63.3. Extract Analyze Command with Workspace Loading Logic

**Status:** pending  
**Dependencies:** 63.1, 63.2  

Extract the analyze command (lines 49-121) into AnalyzeCommand.cs, which has unique workspace loading and multi-stage analysis processing that differs from other commands.

**Details:**

Create `AiCodeGraph.Cli/Commands/AnalyzeCommand.cs` implementing ICommandHandler. Extract the analyze command definition including solutionArgument, solutionOption, outputOption, verboseOption, saveBaselineOption, embeddingEngineOption, embeddingModelOption, embeddingDimensionsOption. The SetAction handler calls LoadWorkspaceStage, ExtractCodeModelStage, BuildCallGraphStage, ComputeMetricsStage, NormalizeMethodsStage, GenerateEmbeddingsStage, CreateEmbeddingEngine, StoreResultsStage, DetectDuplicatesStage, SaveBaselineStage, PrintAnalysisSummary from AnalysisStageHelpers. Use HandleCommandError from OutputHelpers. Reference VectorIndexCache.Invalidate after analysis. Include proper using statements for AiCodeGraph.Core namespaces, System.Diagnostics, and the new Helpers classes.

### 63.4. Extract Query Commands (callgraph, hotspots, tree, dead-code, impact)

**Status:** pending  
**Dependencies:** 63.1, 63.2  

Extract the five query-oriented commands that share common database access patterns and method/node traversal logic into their respective command handler classes.

**Details:**

Create five command files in Commands/: `CallgraphCommand.cs` (lines 123-229) - uses BFS traversal with depth option, direction option, format output. `HotspotsCommand.cs` (lines 231-295) - queries storage.GetHotspotsWithThresholdAsync, formats table/json output. `TreeCommand.cs` (lines 297-500) - complex filtering with nsFilterOption, typeFilterOption, includePrivateOption, skipTestsOption, skipInterfacesOption, compactOption; handles compact markdown output mode. `DeadCodeCommand.cs` (lines 1524-1586) - queries storage.GetDeadCodeAsync with includeOverrides option. `ImpactCommand.cs` (lines 1386-1522) - uses BFS for transitive caller analysis with unlimited depth support and entry point detection. Each command should use PrintCallTree helper where applicable and follow the database exists check pattern with Environment.ExitCode = 1 on error.

### 63.5. Extract Search and Similarity Commands (similar, token-search, semantic-search, duplicates, clusters)

**Status:** pending  
**Dependencies:** 63.1, 63.2  

Extract the five commands related to vector embeddings, similarity search, and code clone detection that share VectorIndexCache and embedding engine usage patterns.

**Details:**

Create five command files: `SimilarCommand.cs` (lines 502-583) - uses VectorIndexCache.GetOrBuild for kNN search on method embeddings. `TokenSearchCommand.cs` (lines 736-827) - uses HashEmbeddingEngine for query vector, VectorIndexCache for search with threshold filtering. `SemanticSearchCommand.cs` (lines 829-924) - uses CreateEmbeddingEngine based on stored engine metadata, warns about hash-based limitations. `DuplicatesCommand.cs` (lines 585-670) - queries storage.GetClonePairsAsync with type/concept filters, formats clone pairs. `ClustersCommand.cs` (lines 672-734) - queries storage.GetClustersAsync, formats cluster information with member method names. All use consistent threshold, top, and format options. Import VectorIndexCache from Helpers namespace.

### 63.6. Extract Export and Analysis Commands (export, drift, coupling, churn, diff)

**Status:** pending  
**Dependencies:** 63.1, 63.2  

Extract the five commands focused on data export, architectural drift detection, coupling metrics, churn analysis, and git diff integration.

**Details:**

Create five command files: `ExportCommand.cs` (lines 926-997) - exports methods and relationships in JSON/CSV format with concept filtering; use CsvEscape from OutputHelpers. `DriftCommand.cs` (lines 999-1140) - uses DriftDetector with configurable thresholds, outputs summary/detail/json formats with Environment.ExitCode = 1 on drift detection. `CouplingCommand.cs` (lines 1658-1726) - uses CouplingAnalyzer for namespace/type level metrics with instability calculations. `ChurnCommand.cs` (lines 1588-1656) - uses ChurnAnalyzer with git since option for change-frequency analysis. `DiffCommand.cs` (lines 1728-1828) - uses GetChangedCsFiles helper from GitHelpers, correlates with database methods. All follow consistent pattern for database existence checking and format option handling.

### 63.7. Extract Integration Commands (context, mcp, setup-claude)

**Status:** pending  
**Dependencies:** 63.1, 63.2  

Extract the three integration-focused commands: context (comprehensive method info for Claude Code), mcp (JSON-RPC server mode), and setup-claude (scaffolds Claude integration files).

**Details:**

Create three command files: `ContextCommand.cs` (lines 1142-1384) - the most complex single command showing method metrics, callers, callees, cluster membership, duplicates, test coverage, source snippet, and git blame; uses FormatAge from OutputHelpers, spawns git processes for timestamp lookups. `McpCommand.cs` (lines 1848-1860) - simple wrapper instantiating McpServer and calling RunAsync. `SetupClaudeCommand.cs` (lines 1862-2143) - creates .claude/commands/cg/ directory structure, writes 11 slash command markdown files, creates .mcp.json, appends to CLAUDE.md; this is the longest single command at ~280 lines. All must preserve exact file content generation for setup-claude to maintain compatibility with existing Claude Code integrations.

### 63.8. Reduce Program.cs to Entry Point and Validate Complete Refactoring

**Status:** pending  
**Dependencies:** 63.1, 63.2, 63.3, 63.4, 63.5, 63.6, 63.7  

Replace the entire Program.cs content with a minimal entry point that delegates to CommandRegistry, then run comprehensive verification to ensure no CLI interface or behavior regression.

**Details:**

Replace Program.cs content with approximately 8 lines: `using System.CommandLine; using System.CommandLine.Parsing; using AiCodeGraph.Cli.Commands; var rootCommand = CommandRegistry.Build(); var parseResult = CommandLineParser.Parse(rootCommand, args); return await parseResult.InvokeAsync();`. Ensure all necessary usings are in CommandRegistry.cs. Run full build verification: `dotnet build`. Run full test suite: `dotnet test` (all 303 tests must pass). Generate help text diff for all 19 commands comparing before/after refactoring. Verify exit codes: success (0) and error (1) cases. Update CommandRegistry to register commands in the exact order from original Program.cs. Clean up any orphaned code references.
