# Task ID: 46

**Title:** Coupling Command - Afferent/Efferent Metrics

**Status:** done

**Dependencies:** 20 ✓, 30 ✓, 31 ✓, 32 ✓, 33 ✓, 35 ✓, 37 ✓, 38 ✓, 39 ✓, 40 ✓, 41 ✓

**Priority:** medium

**Description:** Create a new 'coupling' command with CouplingAnalyzer that computes Ca, Ce, Instability, Abstractness, and Distance from Main Sequence at namespace or type level.

**Details:**

Create new file: AiCodeGraph.Core/Analysis/CouplingAnalyzer.cs

```csharp
namespace AiCodeGraph.Core.Analysis;

public record CouplingMetrics(
    string Name,
    int AfferentCoupling,   // Ca - incoming dependencies
    int EfferentCoupling,   // Ce - outgoing dependencies  
    float Instability,       // I = Ce / (Ca + Ce)
    float Abstractness,      // A = abstract types / total types
    float DistanceFromMain   // D = |A + I - 1|
);

public class CouplingAnalyzer
{
    public async Task<List<CouplingMetrics>> AnalyzeAsync(
        IStorageService storage,
        string level, // "namespace" or "type"
        CancellationToken ct)
    {
        // 1. Get all method calls
        var allMethods = await storage.GetMethodsForExportAsync(null, ct);
        var allCalls = await GetAllCallsAsync(storage, ct);
        
        // 2. Group methods by namespace or type
        var groups = GroupByLevel(allMethods, level);
        
        // 3. For each group, compute Ca and Ce
        var metrics = new List<CouplingMetrics>();
        foreach (var (name, memberIds) in groups)
        {
            var memberSet = new HashSet<string>(memberIds);
            int ca = 0, ce = 0;
            
            foreach (var (callerId, calleeId) in allCalls)
            {
                var callerInGroup = memberSet.Contains(callerId);
                var calleeInGroup = memberSet.Contains(calleeId);
                
                if (callerInGroup && !calleeInGroup) ce++; // outgoing
                if (!callerInGroup && calleeInGroup) ca++; // incoming
            }
            
            var instability = (ca + ce) > 0 ? (float)ce / (ca + ce) : 0f;
            // Abstractness would need type info (interfaces/abstract classes)
            var abstractness = 0f; // Compute from type metadata
            var distance = Math.Abs(abstractness + instability - 1f);
            
            metrics.Add(new CouplingMetrics(name, ca, ce, instability, abstractness, distance));
        }
        
        return metrics.OrderByDescending(m => m.EfferentCoupling).ToList();
    }
}
```

Register CLI command with --level, --db, --format, --top options.
Add MCP tool `cg_coupling` and slash command `/cg:coupling`.

**Test Strategy:**

Create CouplingAnalyzerTests.cs. Test with fixture solution that has cross-project/namespace dependencies. Verify Ca/Ce counts are correct. Verify Instability formula. Test namespace-level vs type-level grouping. Test isolated namespace (Ca=0, Ce=0). Test JSON output format.

## Subtasks

### 46.1. Create CouplingAnalyzer class with namespace/type grouping logic

**Status:** pending  
**Dependencies:** None  

Create AiCodeGraph.Core/Coupling/CouplingAnalyzer.cs with the CouplingMetrics record and CouplingAnalyzer class. Implement method grouping by namespace or type level by parsing fully qualified method IDs from StorageService.GetMethodsForExportAsync(). Add helper to extract namespace or containing type from method IDs.

**Details:**

Create directory AiCodeGraph.Core/Coupling/ and add CouplingAnalyzer.cs. Define the CouplingMetrics record with Name, AfferentCoupling (Ca), EfferentCoupling (Ce), Instability, Abstractness, and DistanceFromMain fields. Implement GroupByLevel() method that takes the list of method export data and groups method IDs by either their namespace or containing type name, extracted from the FullName property. Use the existing GetMethodsForExportAsync storage method to retrieve all methods. The grouping logic should parse the fully qualified name to extract the namespace (everything before the last dot-separated type.method) or the type (namespace.TypeName portion).

### 46.2. Implement Ca/Ce counting from call graph edges

**Status:** pending  
**Dependencies:** 46.1  

Implement the core coupling computation that retrieves all call graph edges from storage and counts afferent (incoming) and efferent (outgoing) dependencies for each namespace/type group by comparing caller/callee membership across groups.

**Details:**

Add a method to retrieve all call edges - use StorageService.GetCallGraphForMethodsAsync() with the full set of method IDs, or add a new GetAllCallEdgesAsync() method to StorageService if needed. For each group, build a HashSet of member method IDs. Iterate all (callerId, calleeId) edges: if caller is in group and callee is not, increment Ce (outgoing); if caller is not in group and callee is, increment Ca (incoming). This is O(edges * groups) but acceptable for typical codebases. Deduplicate edges at the namespace/type boundary level so multiple method-level calls between the same two namespaces count appropriately (each unique method-to-method edge counts once).

### 46.3. Compute Instability, Abstractness, and Distance from Main Sequence

**Status:** pending  
**Dependencies:** 46.2  

Implement the derived metrics: Instability I = Ce/(Ca+Ce), Abstractness A = abstract types/total types in group, and Distance from Main Sequence D = |A + I - 1|. Handle edge cases like zero denominators.

**Details:**

Instability: compute as (float)Ce / (Ca + Ce), returning 0f when both are zero (stable with no coupling). For Abstractness: use the GetTreeAsync storage method which returns TypeKind for each type. Count types where TypeKind is 'Interface' or 'Abstract' vs total types in the group. If no type metadata is available, default Abstractness to 0f. Distance from Main Sequence: Math.Abs(abstractness + instability - 1f). Values close to 0 indicate the group is on the ideal balance line. Sort results by EfferentCoupling descending by default. Return the complete List<CouplingMetrics> from AnalyzeAsync.

### 46.4. Register CLI command with options and output formatters

**Status:** pending  
**Dependencies:** 46.3  

Add the 'coupling' command to Program.cs with --level (namespace|type), --db, --format (table|json), and --top options. Implement table and JSON output formatting following existing command patterns.

**Details:**

In AiCodeGraph.Cli/Program.cs, create: var couplingCommand = new Command('coupling', 'Analyze afferent/efferent coupling metrics'). Add options: --level (string, default 'namespace', choices namespace|type), --db (string, default './ai-code-graph/graph.db'), --format (string, default 'table', choices table|json), --top (int, default 20). In SetAction handler: validate db exists, open StorageService, call CouplingAnalyzer.AnalyzeAsync(), take top N results, format as table (columns: Name, Ca, Ce, I, A, D) or JSON (camelCase). Register with rootCommand.Add(couplingCommand). Follow the exact pattern of hotspots command for structure.

### 46.5. Add MCP tool, slash command, and unit tests

**Status:** pending  
**Dependencies:** 46.4  

Register cg_get_coupling MCP tool in McpServer.cs with level/top/format parameters. Create .claude/commands/cg:coupling.md slash command. Create CouplingAnalyzerTests.cs with unit tests covering grouping, Ca/Ce counting, metric formulas, and edge cases.

**Details:**

MCP: In McpServer.cs HandleToolsList, add cg_get_coupling tool definition with properties: level (string, default namespace), top (integer, default 20), format (string, default json). In HandleToolCall, add case for cg_get_coupling that opens storage, runs CouplingAnalyzer.AnalyzeAsync, returns JSON results. Slash command: Create .claude/commands/cg:coupling.md following existing pattern - runs 'ai-code-graph coupling --db {dbPath} --level namespace --format table'. Tests: Create AiCodeGraph.Tests/CouplingAnalyzerTests.cs. Test with in-memory StorageService populated with known methods and call edges. Verify Ca/Ce counts for cross-namespace dependencies. Test namespace vs type level grouping. Test isolated namespace. Test formula edge cases (zero denominator). Test sorting order.
