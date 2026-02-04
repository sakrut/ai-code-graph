# Task ID: 16

**Title:** Implement Context CLI Subcommand

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Add a `context` CLI subcommand that returns a compact combined summary for a given method - complexity, callers, callees, cluster membership, and duplicates in a single call. This minimizes round-trips and context window usage for Claude Code integration.

**Details:**

Implementation in AiCodeGraph.Cli/Program.cs following existing command patterns:

1. Add a new `Argument<string>` for the method pattern and reuse the existing `dbOption`:
```csharp
var methodPatternArg = new Argument<string>("method-pattern") { Description = "Method name or pattern to search for" };
var contextCommand = new Command("context", "Get combined method context (complexity, callers, callees, cluster, duplicates)") { methodPatternArg, dbOption };
```

2. In the SetAction handler:
   a. Open the database with `StorageService.OpenAsync()`
   b. Call `storage.SearchMethodsAsync(pattern)` to find matching methods
   c. If no match, print "Method not found" and list similar methods as suggestions (use LIKE query)
   d. If multiple matches, list them and let user know to be more specific
   e. For the matched method, gather all context in parallel:
      - `storage.GetMethodInfoAsync(methodId)` for basic info (file, line)
      - `storage.GetHotspotsWithThresholdAsync()` filtered to the method ID, or add a new query `GetMetricsForMethodAsync(methodId)` that returns (Complexity, LOC, Nesting) for a single method
      - `storage.GetCallersAsync(methodId)` for callers list
      - `storage.GetCalleesAsync(methodId)` for callees list
      - `storage.GetClustersAsync()` then filter for clusters containing this method
      - `storage.GetClonePairsAsync()` then filter for pairs involving this method

3. Add a helper query to StorageService for efficiency:
```csharp
public async Task<(int Complexity, int Loc, int Nesting)?> GetMetricsForMethodAsync(string methodId, CancellationToken ct = default)
{
    EnsureConnection();
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "SELECT CognitiveComplexity, LinesOfCode, NestingDepth FROM Metrics WHERE MethodId = @id";
    cmd.Parameters.AddWithValue("@id", methodId);
    using var reader = await cmd.ExecuteReaderAsync(ct);
    if (await reader.ReadAsync(ct))
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    return null;
}

public async Task<(string Label, int MemberCount, float Cohesion)?> GetClusterForMethodAsync(string methodId, CancellationToken ct = default)
{
    EnsureConnection();
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = @"
        SELECT ic.Label, ic.MemberCount, ic.Cohesion
        FROM MethodClusterMap mcm
        JOIN IntentClusters ic ON ic.Id = mcm.ClusterId
        WHERE mcm.MethodId = @id";
    cmd.Parameters.AddWithValue("@id", methodId);
    using var reader = await cmd.ExecuteReaderAsync(ct);
    if (await reader.ReadAsync(ct))
        return (reader.GetString(0), reader.GetInt32(1), reader.GetFloat(2));
    return null;
}

public async Task<List<(string MethodId, float Score)>> GetDuplicatesForMethodAsync(string methodId, CancellationToken ct = default)
{
    EnsureConnection();
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = @"
        SELECT CASE WHEN MethodIdA = @id THEN MethodIdB ELSE MethodIdA END, HybridScore
        FROM ClonePairs
        WHERE (MethodIdA = @id OR MethodIdB = @id)
        ORDER BY HybridScore DESC";
    cmd.Parameters.AddWithValue("@id", methodId);
    var results = new List<(string, float)>();
    using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
        results.Add((reader.GetString(0), reader.GetFloat(1)));
    return results;
}
```

4. Format output exactly as specified in the PRD (compact plain text):
```
Method: Namespace.Type.Method(params)
File: path/to/file.cs:42
Complexity: CC=12 LOC=35 Nesting=3
Callers (3): CallerA, CallerB, CallerC
Callees (2): CalleeX, CalleeY
Cluster: "cluster-label" (N members, cohesion: 0.XX)
Duplicates: MethodA (score: 0.95), MethodB (score: 0.82)
```

5. Omit Cluster/Duplicates lines if none exist. Use short method names (just `Type.Method`) for callers/callees to keep output compact.

**Test Strategy:**

1. Add unit tests in a new `ContextCommandTests.cs`:
   - Test with a method that has all attributes (callers, callees, cluster, duplicates)
   - Test with a method that has no cluster or duplicates (verify those lines are omitted)
   - Test with a non-existent method pattern (verify 'Method not found' and suggestions)
   - Test with ambiguous pattern matching multiple methods
2. Add integration test using the TestSolution fixture database:
   - Analyze the fixture, then run context command against a known method
   - Verify output format matches the PRD spec exactly
3. Verify the new StorageService helper methods with in-memory database tests
