# Task ID: 41

**Title:** CLI Layer Tests via System.CommandLine Test Infrastructure

**Status:** done

**Dependencies:** 27 ✓

**Priority:** medium

**Description:** Add tests that invoke CLI commands programmatically using System.CommandLine's test infrastructure, verifying command parsing, help text, and error handling.

**Details:**

Create new file: AiCodeGraph.Tests/CliCommandTests.cs

```csharp
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Xunit;

namespace AiCodeGraph.Tests;

public class CliCommandTests
{
    // Note: System.CommandLine 2.0.2 uses SetAction pattern
    // We test by building the root command and invoking with test args
    
    [Fact]
    public async Task HotspotsCommand_WithValidDb_ReturnsZero()
    {
        // Create a temp SQLite DB with test data
        // Invoke: rootCommand.InvokeAsync("hotspots --db {tempDb}")
        // Assert exit code 0
    }
    
    [Fact]
    public async Task HotspotsCommand_MissingDb_ReturnsNonZero()
    {
        // Invoke with non-existent DB path
        // Assert exit code != 0 or error output
    }
    
    [Fact]
    public async Task TreeCommand_WithNamespaceFilter_Works() { /* ... */ }
    
    [Fact]
    public async Task CallgraphCommand_RequiresMethod_ShowsError() { /* ... */ }
    
    [Fact]
    public async Task ContextCommand_ValidMethod_ShowsOutput() { /* ... */ }
    
    [Fact]
    public async Task AnalyzeCommand_HelpText_ShowsAllOptions() { /* ... */ }
    
    [Theory]
    [InlineData("hotspots")]
    [InlineData("tree")]
    [InlineData("callgraph")]
    [InlineData("similar")]
    [InlineData("duplicates")]
    [InlineData("clusters")]
    [InlineData("export")]
    [InlineData("drift")]
    [InlineData("context")]
    public async Task Command_Help_ShowsDescription(string commandName)
    {
        // Invoke: rootCommand.InvokeAsync("{commandName} --help")
        // Assert output contains command description
    }
}
```

For mocking storage, use IStorageService interface (from task 27) to create a mock implementation for testing command logic without a real database.

**Test Strategy:**

Each test creates a fresh command tree, invokes with specific args, captures stdout/stderr via TestConsole, and asserts exit code and output. Use temp directories for DB files. Clean up after each test. Run with dotnet test.

## Subtasks

### 41.1. Set Up CLI Test Infrastructure with System.CommandLine 2.0.2 Invocation

**Status:** pending  
**Dependencies:** None  

Create the test file and establish infrastructure for programmatically invoking CLI commands using System.CommandLine 2.0.2's InvokeAsync, including extracting the root command builder from Program.cs into a testable static method.

**Details:**

1. Refactor AiCodeGraph.Cli/Program.cs to extract command tree building into a public static method (e.g., `public static RootCommand BuildRootCommand()`) so tests can access the full command tree without running the application entry point.
2. Add a project reference from AiCodeGraph.Tests to AiCodeGraph.Cli in the test .csproj file.
3. Add System.CommandLine NuGet package reference to the test project if not already transitively available.
4. Create AiCodeGraph.Tests/CliCommandTests.cs with a base test helper that:
   - Builds the root command via `Program.BuildRootCommand()` (or equivalent)
   - Provides a helper method to invoke commands with string args and capture exit code, stdout, and stderr using System.CommandLine's `TestConsole` or `StringWriter` redirection
5. Create a `TestDatabaseHelper` class that creates temp SQLite databases with seeded data (methods, metrics, call graph edges, embeddings, clone pairs) using the existing StorageService with file-based temp paths, since commands read from file paths not in-memory databases.
6. Implement IAsyncDisposable cleanup for temp database files in the test class.

### 41.2. Write Help Text and Option Parsing Tests for All Commands

**Status:** pending  
**Dependencies:** 41.1  

Create Theory-based tests that verify --help output and option/argument parsing for all 11+ CLI commands (analyze, callgraph, hotspots, tree, similar, duplicates, clusters, search, export, drift, context, mcp, setup-claude).

**Details:**

1. Write a [Theory] test with [InlineData] for each command name that invokes `{commandName} --help` and asserts:
   - Exit code is 0
   - Output contains the command's description string
   - Output lists expected options (e.g., --db, --top, --threshold for hotspots)
2. Write individual [Fact] tests for complex option parsing:
   - `analyze` command: verify --solution argument is recognized
   - `callgraph`: verify --depth, --direction, --format options parse correctly
   - `hotspots`: verify --top and --threshold defaults
   - `tree`: verify --namespace and --type filter options
   - `similar`: verify method argument and --top option
   - `duplicates`: verify --threshold, --type, --concept options
   - `export`: verify --format option accepts json/csv
   - `drift`: verify --baseline and --db options
   - `context`: verify method argument is required
3. Test that unrecognized options produce non-zero exit codes and error messages.
4. Use the TestConsole or captured output pattern established in subtask 1.

### 41.3. Write Tests for Commands with Valid Temp Databases

**Status:** pending  
**Dependencies:** 41.1  

Create integration-style tests that invoke commands (hotspots, tree, context, callgraph, similar, duplicates, clusters, export) against pre-seeded temp SQLite databases and verify successful execution with expected output.

**Details:**

1. Use TestDatabaseHelper from subtask 1 to create temp databases with realistic test data:
   - Methods with varying complexity scores (for hotspots)
   - Namespace/type hierarchy (for tree)
   - Call graph edges between methods (for callgraph)
   - Embeddings stored for methods (for similar, search)
   - Clone pairs with scores (for duplicates)
   - Cluster assignments (for clusters)
   - Full method info with callers/callees/cluster/duplicates (for context)
2. Write [Fact] tests:
   - `HotspotsCommand_WithValidDb_ReturnsZero`: Invoke with --db tempPath, assert exit 0, output contains method names
   - `HotspotsCommand_WithThreshold_FiltersResults`: Invoke with --threshold 10, assert only high-complexity methods shown
   - `TreeCommand_WithValidDb_ShowsHierarchy`: Assert output contains namespace structure
   - `TreeCommand_WithNamespaceFilter_FiltersCorrectly`: Apply --namespace filter
   - `ContextCommand_ValidMethod_ShowsDetails`: Invoke with known method name, assert output includes complexity, callers, callees
   - `CallgraphCommand_ValidMethod_ShowsRelationships`: Invoke with depth and direction options
   - `SimilarCommand_ValidMethod_FindsMatches`: Invoke with method that has embeddings
   - `DuplicatesCommand_ShowsClonePairs`: Assert output lists clone pairs above threshold
   - `ClustersCommand_ShowsGroupings`: Assert cluster output
   - `ExportCommand_JsonFormat_ProducesValidJson`: Invoke with --format json, verify output is parseable JSON
3. Each test should create its own temp DB, invoke the command, and clean up via IAsyncDisposable.

### 41.4. Write Tests for Error Cases - Missing DB, Invalid Args, and Exit Codes

**Status:** pending  
**Dependencies:** 41.1  

Create tests verifying proper error handling when commands receive invalid inputs: missing database files, missing required arguments, invalid option values, and non-existent method names.

**Details:**

1. Write tests for missing/invalid database path:
   - `HotspotsCommand_MissingDb_ReturnsNonZero`: Invoke with non-existent --db path, assert exit code != 0
   - `TreeCommand_InvalidDbPath_ShowsError`: Assert stderr contains meaningful error message
   - `ContextCommand_MissingDb_ReturnsNonZero`: Same pattern for context command
   - `ExportCommand_MissingDb_ReturnsNonZero`: Same for export
2. Write tests for missing required arguments:
   - `CallgraphCommand_NoMethod_ShowsError`: Invoke without method argument, assert error
   - `ContextCommand_NoMethod_ShowsError`: Same for context
   - `SimilarCommand_NoMethod_ShowsError`: Same for similar
   - `AnalyzeCommand_NoSolution_ShowsError`: No solution argument
3. Write tests for invalid option values:
   - `HotspotsCommand_InvalidTopValue_ShowsError`: --top with non-numeric value
   - `CallgraphCommand_InvalidDirection_HandlesGracefully`: --direction with invalid string
   - `ExportCommand_InvalidFormat_ShowsError`: --format with unsupported value
4. Write tests for valid DB but missing data:
   - `ContextCommand_NonExistentMethod_HandlesGracefully`: Method not in DB, assert graceful handling
   - `CallgraphCommand_UnknownMethod_ShowsNoResults`: Method has no call edges
5. All error tests should verify:
   - Non-zero exit code OR error message in output (depending on command implementation)
   - No unhandled exceptions (no stack traces in output)
   - Error messages are user-friendly and actionable
