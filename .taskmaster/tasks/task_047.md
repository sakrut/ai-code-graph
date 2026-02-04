# Task ID: 47

**Title:** Churn Command - Git Frequency and Complexity Analysis

**Status:** done

**Dependencies:** 34 ✓

**Priority:** medium

**Description:** Create a new 'churn' command with ChurnAnalyzer that combines git change frequency with cognitive complexity to identify high-risk methods.

**Details:**

Create new file: AiCodeGraph.Core/Analysis/ChurnAnalyzer.cs

```csharp
using System.Diagnostics;

namespace AiCodeGraph.Core.Analysis;

public record ChurnResult(
    string MethodId,
    string MethodName,
    string? FilePath,
    int Changes,
    int CognitiveComplexity,
    float ChurnScore  // Changes * CC
);

public class ChurnAnalyzer
{
    public async Task<List<ChurnResult>> AnalyzeAsync(
        IStorageService storage,
        string since,  // git date format, e.g., "6 months ago"
        int top,
        CancellationToken ct)
    {
        var results = new List<ChurnResult>();
        var methods = await storage.GetMethodsForExportAsync(null, ct);
        
        // Group methods by file for efficient git log calls
        var byFile = methods
            .Where(m => m.Item10 != null) // has FilePath (tuple position varies)
            .GroupBy(m => m.Item10!);
        
        foreach (var fileGroup in byFile)
        {
            var filePath = fileGroup.Key;
            if (!File.Exists(filePath)) continue;
            
            // Get commit count for this file since date
            var commitCount = await GetCommitCount(filePath, since, ct);
            if (commitCount == 0) continue;
            
            foreach (var method in fileGroup)
            {
                var metrics = await storage.GetMethodMetricsAsync(method.Item1, ct);
                if (metrics == null) continue;
                
                var cc = metrics.Value.CognitiveComplexity;
                var churn = commitCount * cc;
                
                results.Add(new ChurnResult(
                    method.Item1, method.Item3, filePath,
                    commitCount, cc, churn));
            }
        }
        
        return results.OrderByDescending(r => r.ChurnScore).Take(top).ToList();
    }
    
    private async Task<int> GetCommitCount(string filePath, string since, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"log --oneline --since=\"{since}\" -- \"{filePath}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return 0;
        
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
```

Register CLI command with --since, --db, --format, --top options.
Add MCP tool `cg_churn` and slash command `/cg:churn`.

**Test Strategy:**

Create ChurnAnalyzerTests.cs. Integration test with git fixture: create commits over time, verify churn scores. Test --since filter. Test file with no changes returns 0. Mock Process.Start for unit tests. Verify output sorting by churn score. Test JSON format.

## Subtasks

### 47.1. Create ChurnAnalyzer class with file-grouped git log execution

**Status:** pending  
**Dependencies:** None  

Implement the ChurnAnalyzer class in AiCodeGraph.Core/Analysis/ChurnAnalyzer.cs with the ChurnResult record and the AnalyzeAsync method that groups methods by file path for efficient git log calls.

**Details:**

Create AiCodeGraph.Core/Analysis/ChurnAnalyzer.cs containing:
1. ChurnResult record with MethodId, MethodName, FilePath, Changes, CognitiveComplexity, and ChurnScore fields
2. ChurnAnalyzer class with AnalyzeAsync method that takes IStorageService, since string, top int, and CancellationToken
3. Group methods by file path using storage.GetMethodsForExportAsync(), filter to methods with non-null file paths
4. For each file group, call GetCommitCount helper, skip files with 0 commits
5. For each method in group, retrieve metrics via storage.GetMethodMetricsAsync() and compute churn score as commitCount * cognitiveComplexity
6. Return results ordered descending by ChurnScore, taking top N
7. Handle missing files gracefully (skip if File.Exists returns false)

### 47.2. Implement git commit counting with --since parameter and process management

**Status:** pending  
**Dependencies:** 47.1  

Implement the GetCommitCount private method that spawns git log processes with --since filtering, handles process lifecycle, and parses output line counts.

**Details:**

Implement GetCommitCount in ChurnAnalyzer:
1. Create ProcessStartInfo with FileName='git', Arguments formatted as: log --oneline --since="{since}" -- "{filePath}"
2. Set RedirectStandardOutput=true, UseShellExecute=false, CreateNoWindow=true
3. Start process, handle null process return (return 0)
4. Read StandardOutput to end asynchronously with CancellationToken
5. WaitForExitAsync with CancellationToken support
6. Count non-empty lines in output (split by newline, remove empty entries)
7. Handle edge cases: git not installed (process start fails), non-git directory, file outside repo
8. Consider adding working directory to ProcessStartInfo based on the file's directory to ensure git finds the repo
9. Follow patterns from task 34's git process management for consistency

### 47.3. Register CLI churn command with options and formatted output

**Status:** pending  
**Dependencies:** 47.1, 47.2  

Add the 'churn' command to Program.cs with --since, --db, --format, and --top options, implementing table and JSON output formats showing churn scores combining git frequency with cognitive complexity.

**Details:**

In AiCodeGraph.Cli/Program.cs:
1. Create churn command: new Command("churn", "Identify high-risk methods by combining git change frequency with complexity")
2. Add options:
   - --since: Option<string> with default "6 months ago" (git date format)
   - --db: Option<string> with default "./ai-code-graph/graph.db"
   - --format: Option<string> with default "table", choices: table|json
   - --top: Option<int> with default 20
3. SetAction handler that:
   a. Opens StorageService with the db path using OpenAsync()
   b. Creates ChurnAnalyzer and calls AnalyzeAsync with parameters
   c. For table format: display ranked list with columns for Rank, Method, File, Changes, CC, ChurnScore
   d. For JSON format: serialize results with System.Text.Json
   e. Handle empty results gracefully with informative message
4. Add command to root command

### 47.4. Add MCP tool cg_churn, slash command /cg:churn, and comprehensive tests

**Status:** pending  
**Dependencies:** 47.1, 47.2, 47.3  

Register the cg_churn MCP tool and /cg:churn slash command following existing patterns, and create ChurnAnalyzerTests.cs with unit and integration tests using a git fixture with timestamped commits.

**Details:**

1. MCP Tool (follow existing patterns from other cg_ tools):
   - Register cg_churn tool with parameters: since (string, optional), top (int, optional), format (string, optional)
   - Tool description: 'Identify high-risk methods by combining git change frequency with cognitive complexity'
   - Implementation calls ChurnAnalyzer.AnalyzeAsync and returns formatted results

2. Slash Command:
   - Create .claude/commands/cg_churn.md following the pattern of existing slash commands
   - Command: /cg:churn with optional arguments for since, top parameters

3. Tests in AiCodeGraph.Tests/ChurnAnalyzerTests.cs:
   - Unit test: Mock IStorageService, verify grouping by file, verify score calculation (changes * CC)
   - Unit test: Verify ordering by ChurnScore descending
   - Unit test: Verify top parameter limits output
   - Unit test: Methods without file paths are skipped
   - Unit test: Files that don't exist are skipped
   - Integration test: Create temp git repo with multiple commits at different timestamps, run full analysis, verify counts
   - Integration test: Test --since filter excludes old commits
   - Test JSON output format is valid JSON
   - Test empty result when no methods have changes
