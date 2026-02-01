# Task ID: 20

**Title:** SQL WHERE Clause Optimization in GetCallGraphForMethodsAsync

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Replace the full table scan in GetCallGraphForMethodsAsync with a parameterized SQL WHERE clause using IN operators, chunking for SQLite's 999 parameter limit.

**Details:**

File: AiCodeGraph.Core/Storage/StorageService.cs lines 748-764

Current implementation loads ALL MethodCalls rows and filters in-memory with methodIds.Contains(). Replace with:

```csharp
public async Task<List<(string CallerId, string CalleeId)>> GetCallGraphForMethodsAsync(HashSet<string> methodIds, CancellationToken ct)
{
    var results = new List<(string, string)>();
    var idList = methodIds.ToList();
    const int chunkSize = 450; // 450 * 2 = 900 params (under 999 limit)
    
    for (int i = 0; i < idList.Count; i += chunkSize)
    {
        var chunk = idList.Skip(i).Take(chunkSize).ToList();
        using var cmd = _connection!.CreateCommand();
        
        var callerParams = string.Join(",", chunk.Select((_, idx) => $"@c{idx}"));
        var calleeParams = string.Join(",", chunk.Select((_, idx) => $"@e{idx}"));
        
        cmd.CommandText = $"SELECT CallerId, CalleeId FROM MethodCalls WHERE CallerId IN ({callerParams}) OR CalleeId IN ({calleeParams})";
        
        for (int j = 0; j < chunk.Count; j++)
        {
            cmd.Parameters.AddWithValue($"@c{j}", chunk[j]);
            cmd.Parameters.AddWithValue($"@e{j}", chunk[j]);
        }
        
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add((reader.GetString(0), reader.GetString(1)));
    }
    return results;
}
```

Key constraint: SQLite limit of 999 parameters. Since we use both CallerId IN and CalleeId IN, chunk at 450 IDs per batch (450*2=900 < 999). Deduplicate results across chunks if needed.

**Test Strategy:**

Add tests in StorageServiceTests.cs: (1) Verify same results as previous implementation with small dataset. (2) Test with exactly 900 method IDs to verify chunking boundary. (3) Test with 1000+ IDs to verify multi-chunk works. (4) Verify empty methodIds returns empty list. (5) Performance comparison: log query count before/after.

## Subtasks

### 20.1. Implement chunked parameterized SQL query in GetCallGraphForMethodsAsync

**Status:** pending  
**Dependencies:** None  

Replace the full table scan in StorageService.cs:748-764 with a chunked parameterized SQL WHERE clause using IN operators, respecting SQLite's 999 parameter limit by chunking at 450 IDs per batch.

**Details:**

In AiCodeGraph.Core/Storage/StorageService.cs, replace the current implementation at lines 748-764 that does `SELECT CallerId, CalleeId FROM MethodCalls` followed by in-memory filtering with `methodIds.Contains()`. The new implementation should: (1) Convert methodIds HashSet to a List for indexed access, (2) Define `const int chunkSize = 450` (450*2=900 params, under SQLite's 999 limit), (3) Loop through idList in chunks of 450, (4) For each chunk, build a parameterized SQL command with `WHERE CallerId IN (@c0,@c1,...) OR CalleeId IN (@e0,@e1,...)`, (5) Add parameters using `cmd.Parameters.AddWithValue()` for both caller and callee parameter sets, (6) Execute the reader and collect results into the list. Handle the empty methodIds case early by returning an empty list immediately.

### 20.2. Handle result deduplication across chunks

**Status:** pending  
**Dependencies:** 20.1  

Add deduplication logic to prevent duplicate (CallerId, CalleeId) tuples when a row matches across multiple chunks of the batched query.

**Details:**

When method IDs are split across multiple chunks, a MethodCalls row where CallerId is in chunk A and CalleeId is in chunk B could appear in both chunk results. To handle this: use a `HashSet<(string, string)>` as the accumulator instead of a plain List, or add a deduplication step before returning. The final return should convert to `List<(string, string)>`. The simplest approach is to use a HashSet during accumulation: `var seen = new HashSet<(string, string)>()` and only add to results if `seen.Add((caller, callee))` returns true. This ensures O(1) dedup without post-processing.

### 20.3. Add unit test for small dataset correctness

**Status:** pending  
**Dependencies:** 20.1  

Add a test in StorageServiceTests.cs verifying GetCallGraphForMethodsAsync returns correct results with a small dataset, matching the behavior of the previous full-scan implementation.

**Details:**

In AiCodeGraph.Tests/StorageServiceTests.cs, add a test method `GetCallGraphForMethodsAsync_ReturnsMatchingEdges`. Setup: call InitializeAsync(), SaveTestModel(), then SaveCallGraphAsync() with edges like (CreateUser->ValidateUser), (ValidateUser->UpdateUser), (UpdateUser->ExternalMethod). Call GetCallGraphForMethodsAsync with a HashSet containing {CreateUser, ValidateUser}. Assert the result contains (CreateUser, ValidateUser) and (ValidateUser, UpdateUser) but NOT (UpdateUser, ExternalMethod) since ExternalMethod is not in the set. Also add a test for empty input returning empty list.

### 20.4. Add unit test for chunking boundary behavior

**Status:** pending  
**Dependencies:** 20.1, 20.2, 20.3  

Add tests verifying correct behavior at and beyond the 450 ID chunk boundary, ensuring multi-chunk queries work correctly and produce deduplicated results.

**Details:**

In StorageServiceTests.cs, add two tests: (1) `GetCallGraphForMethodsAsync_ExactlyChunkSize_WorksCorrectly` - create 450 method IDs and a few call edges among them, verify all matching edges are returned in a single chunk. (2) `GetCallGraphForMethodsAsync_MultipleChunks_ReturnsAllEdges` - create 500+ method IDs with edges spanning chunks (e.g., methodId at index 0 calling methodId at index 460), verify all matching edges are found across chunks. Use programmatic generation of method IDs like `$"method:M{i}"` and insert corresponding MethodCalls rows. These tests don't need full code models - insert directly into MethodCalls table using SaveCallGraphAsync.

### 20.5. Verify callers in Program.cs and McpServer.cs work correctly with optimized method

**Status:** pending  
**Dependencies:** 20.1, 20.2  

Verify that the two call sites of GetCallGraphForMethodsAsync in Program.cs:844 and McpServer.cs:564 continue to work correctly with the optimized implementation.

**Details:**

Review the call sites in AiCodeGraph.Cli/Program.cs (line 844, the callgraph command handler) and AiCodeGraph.Cli/Mcp/McpServer.cs (line 564, the cg_callgraph MCP tool). Both pass a `HashSet<string> methodIds` built from BFS traversal of call relationships. Verify: (1) The method signature hasn't changed (same HashSet<string> parameter, same return type), (2) Build the full solution with `dotnet build` to confirm no compilation errors, (3) Run the existing test suite with `dotnet test` to confirm no regressions. No code changes should be needed at the call sites since the method signature and return type are preserved.
