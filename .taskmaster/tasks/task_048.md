# Task ID: 48

**Title:** Diff Command - Branch/Commit Comparison

**Status:** done

**Dependencies:** 23 ✓, 30 ✓, 31 ✓, 32 ✓, 33 ✓, 35 ✓, 37 ✓, 38 ✓, 39 ✓, 40 ✓, 41 ✓

**Priority:** medium

**Description:** Create a new 'diff' command that compares code graphs between two git refs by identifying changed files and running DriftDetector analysis.

**Details:**

File: AiCodeGraph.Cli/Program.cs (new command registration)

```csharp
var diffCommand = new Command("diff", "Compare code graphs between git refs");
var diffFromOption = new Option<string>("--from", () => "HEAD~1", "Base git ref");
var diffToOption = new Option<string>("--to", () => "HEAD", "Target git ref");
var diffDbOption = new Option<string>("--db", () => "./ai-code-graph/graph.db", "Database path");
var diffFormatOption = new Option<string>("--format", () => "summary", "Output: summary|detail|json");

diffCommand.SetAction(async (parseResult, ct) =>
{
    var fromRef = parseResult.GetValue(diffFromOption)!;
    var toRef = parseResult.GetValue(diffToOption)!;
    var dbPath = parseResult.GetValue(diffDbOption)!;
    
    // Step 1: Get changed files between refs
    var changedFiles = await GetChangedFiles(fromRef, toRef, ct);
    // git diff --name-only fromRef toRef -- "*.cs"
    
    if (changedFiles.Count == 0)
    {
        Console.WriteLine("No C# files changed between refs.");
        return 0;
    }
    
    // Step 2: For simple approach, use file-level change detection
    // Show which methods are in changed files
    using var storage = new StorageService(dbPath);
    await storage.OpenAsync(ct);
    
    var affectedMethods = new List<(string Id, string Name, string File)>();
    foreach (var file in changedFiles)
    {
        var methods = await storage.SearchMethodsAsync($"%{Path.GetFileNameWithoutExtension(file)}%", ct);
        // Better: query by FilePath if stored
        affectedMethods.AddRange(methods.Select(m => (m.Item1, m.Item2, file)));
    }
    
    // Step 3: For detailed mode, could re-analyze to temp DBs and use DriftDetector
    // This is the advanced path from the PRD
    
    // Output affected methods with metrics
    Console.WriteLine($"Changes between {fromRef}..{toRef}:");
    Console.WriteLine($"Files changed: {changedFiles.Count}");
    Console.WriteLine($"Methods affected: {affectedMethods.Count}");
    // ... detail output ...
});
```

Add MCP tool `cg_diff` and slash command `/cg:diff`.

**Test Strategy:**

Integration test with git fixture: create two branches with different code, verify diff shows changes. Test with --from and --to pointing to same ref (no changes). Test with non-existent ref (error handling). Test summary vs detail format. Verify file filtering to .cs only.

## Subtasks

### 48.1. Implement Git Diff File Detection Between Refs

**Status:** pending  
**Dependencies:** None  

Create a utility method that executes 'git diff --name-only' between two git refs and filters results to only .cs files, returning a list of changed C# file paths.

**Details:**

Create a new static helper class (e.g., AiCodeGraph.Core/Git/GitDiffHelper.cs) or add a method to an existing utility class that:
1. Spawns a `git diff --name-only <fromRef> <toRef> -- "*.cs"` process
2. Captures stdout and parses line-by-line into a List<string> of relative file paths
3. Handles error cases: non-existent refs (non-zero exit code), empty diffs, git not installed
4. Accepts a CancellationToken for async cancellation
5. Validates that the current directory is a git repository before running
6. Returns an empty list (not null) when no .cs files changed

Use System.Diagnostics.Process with RedirectStandardOutput and RedirectStandardError. Follow the same pattern used in Task 47's ChurnAnalyzer for process execution.

### 48.2. Create Method-to-File Mapping Query in StorageService

**Status:** pending  
**Dependencies:** 48.1  

Add or extend a StorageService query method that retrieves all methods associated with a given file path, enabling lookup of affected methods when files change.

**Details:**

Extend StorageService (AiCodeGraph.Core/Storage/StorageService.cs) with a method:
```csharp
public async Task<List<(string MethodId, string MethodName, int? CognitiveComplexity)>> GetMethodsByFilePathAsync(string filePath, CancellationToken ct)
```

1. Check if the database schema stores file paths for methods. If FilePath is stored, query directly with WHERE FilePath = @filePath or LIKE pattern matching.
2. If FilePath is not directly stored, fall back to matching by file name without extension against method IDs or names (using Path.GetFileNameWithoutExtension).
3. Include cognitive complexity in the result tuple so the diff output can show metrics.
4. Handle case-insensitive path matching for cross-platform compatibility.
5. Return empty list for files with no matching methods in the database.

### 48.3. Register Diff Command with Options in Program.cs

**Status:** pending  
**Dependencies:** 48.1, 48.2  

Register the 'diff' command in the CLI with --from, --to, --db, and --format options using System.CommandLine 2.0.2 patterns, wiring up the action handler.

**Details:**

In AiCodeGraph.Cli/Program.cs, add the diff command following existing command patterns:
1. Create `var diffCommand = new Command("diff", "Compare code graphs between git refs");`
2. Add options:
   - `--from` (string, default "HEAD~1"): Base git ref
   - `--to` (string, default "HEAD"): Target git ref
   - `--db` (string, default "./ai-code-graph/graph.db"): Database path
   - `--format` (string, default "summary"): Output format (summary|detail|json)
3. Use SetAction with async handler that:
   a. Calls GitDiffHelper to get changed files
   b. Opens StorageService with the db path
   c. For each changed file, queries affected methods using the new StorageService method
   d. Delegates to output formatter based on --format option
   e. Returns exit code 0 on success, non-zero on error
4. Add diffCommand to the root command

### 48.4. Implement Summary, Detail, and JSON Output Formats

**Status:** pending  
**Dependencies:** 48.2, 48.3  

Implement three output modes for the diff command: summary (file/method counts), detail (per-method metrics and caller info), and json (structured output for tooling).

**Details:**

Create output formatting logic (can be inline in the action or a separate formatter class):

**Summary mode (default):**
- Print `Changes between {fromRef}..{toRef}:`
- Print `Files changed: {count}`
- Print `Methods affected: {count}`
- List file names with method counts per file

**Detail mode:**
- Everything in summary, plus:
- For each affected method, show: MethodId, CognitiveComplexity, caller count
- Group methods by file
- Highlight methods with CC > 10 as high-risk changes
- Show total complexity delta if possible

**JSON mode:**
- Output a JSON object with structure: `{ fromRef, toRef, filesChanged: [...], methods: [{ id, name, file, complexity, callers }] }`
- Use System.Text.Json serialization
- Ensure output is valid JSON for piping to other tools

All modes should handle the zero-changes case gracefully with an informative message.

### 48.5. Add MCP Tool, Slash Command, and Integration Tests

**Status:** pending  
**Dependencies:** 48.3, 48.4  

Expose the diff functionality as an MCP tool (cg_diff) and Claude Code slash command (/cg:diff), then create integration tests with git branch fixtures.

**Details:**

1. **MCP Tool (cg_diff):** Follow the pattern established in the existing MCP tool registrations. Register `cg_diff` with parameters: fromRef (string, optional, default HEAD~1), toRef (string, optional, default HEAD), dbPath (string, optional), format (string, optional, default summary). Map to the same logic as the CLI command.

2. **Slash Command (/cg:diff):** Add the slash command definition following the pattern of existing `/cg:*` commands. Should accept optional arguments for from/to refs.

3. **Integration Tests:** Create DiffCommandTests.cs in AiCodeGraph.Tests:
   - Set up a temporary git repository with known commits
   - Create initial commit with .cs files containing methods
   - Create second commit modifying some files
   - Run diff between commits and verify output
   - Test with same ref (no changes)
   - Test with non-existent ref (error case)
   - Test all three format modes
   - Clean up temp repos after tests

4. Update CLAUDE.md slash command list to include `/cg:diff`.
