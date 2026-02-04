# Task ID: 53

**Title:** Fix Dead-Code Detection False Positives for Top-Level Statement Callers

**Status:** done

**Dependencies:** 38 ✓, 26 ✓

**Priority:** medium

**Description:** Fix the CallGraphBuilder to trace method invocations from C# top-level statements by synthesizing an entry-point caller ID, eliminating false positives in dead-code results for methods like StructuralCloneDetector.DetectClones and MetricsEngine.ComputeMetrics that are called from Program.cs.

**Details:**

## Root Cause

`CallGraphBuilder.WalkMethodBodies()` (CallGraphBuilder.cs:66-91) only iterates over `BaseMethodDeclarationSyntax` nodes. C# top-level statements compile to `GlobalStatementSyntax` nodes, which are never visited. Any method called from top-level statements has no caller edge in the `MethodCalls` table, causing `GetDeadCodeAsync()` (StorageService.cs:875-913) to report them as dead code via `LEFT JOIN MethodCalls mc ON m.Id = mc.CalleeId WHERE mc.CallerId IS NULL`.

## Implementation Approach: Synthetic Entry-Point Method ID

Modify `CallGraphBuilder` to detect and process top-level statements with a synthetic caller ID. This is preferred over filtering because it produces a correct call graph that benefits all downstream consumers (dead-code, callgraph, context, coupling).

### Step 1: Define synthetic entry-point constant

In `CallGraphBuilder.cs`, add a constant for the synthetic caller ID:

```csharp
public const string TopLevelEntryPointId = "<Program>.$TopLevelStatements()";
```

### Step 2: Modify `WalkMethodBodies` to handle `GlobalStatementSyntax`

After the existing `BaseMethodDeclarationSyntax` loop in `WalkMethodBodies()`, add processing for global statements:

```csharp
private void WalkMethodBodies(SyntaxNode root, SemanticModel semanticModel)
{
    // Existing method body walking...
    foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
    {
        // ... existing code unchanged ...
    }

    // NEW: Handle top-level statements (C# 9+)
    var globalStatements = root.DescendantNodes().OfType<GlobalStatementSyntax>().ToList();
    if (globalStatements.Count > 0)
    {
        foreach (var globalStatement in globalStatements)
        {
            ResolveInvocationsInBody(TopLevelEntryPointId, globalStatement.Statement, semanticModel);
        }
    }
}
```

### Step 3: Handle local functions within top-level statements

Top-level statements can also contain local functions that call other methods. These local functions are already captured by `BuildSymbolMap` (line 57-62), but their calls from top-level scope need the synthetic caller. Additionally, walk local function bodies defined at the top level:

```csharp
// After global statements processing, also walk top-level local functions
foreach (var localFunc in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
{
    // Only process top-level local functions (parent is GlobalStatementSyntax or CompilationUnit)
    if (localFunc.Parent is GlobalStatementSyntax || localFunc.Parent is CompilationUnitSyntax)
    {
        var symbol = semanticModel.GetDeclaredSymbol(localFunc);
        if (symbol != null)
        {
            var localFuncId = GetSymbolId(symbol);
            if (localFuncId != null)
            {
                // Record that top-level statements call this local function
                _edges.Add(new MethodCallEdge(TopLevelEntryPointId, localFuncId, CallKind.Direct));
                // Walk the local function body for its own callees
                if (localFunc.Body != null)
                    ResolveInvocationsInBody(localFuncId, localFunc.Body, semanticModel);
                else if (localFunc.ExpressionBody != null)
                    ResolveInvocationsInBody(localFuncId, localFunc.ExpressionBody, semanticModel);
            }
        }
    }
}
```

### Step 4: Update `GetDeadCodeAsync` to exclude synthetic entry point

In `StorageService.cs`, add the synthetic ID to the exclusion list so the synthetic entry point itself doesn't appear as dead code:

```csharp
AND m.Id != '<Program>.$TopLevelStatements()'
```

Alternatively, since the synthetic method won't exist in the `Methods` table (it's never extracted as a real method), it won't appear in results. But adding the exclusion is a safety measure.

### Step 5: Ensure synthetic caller doesn't break other commands

The synthetic entry point ID will appear as a caller in `MethodCalls` table. Commands like `callgraph` and `context` that display callers should handle this gracefully:
- The `context` command already handles missing method details (shows ID only)
- The callgraph BFS traversal will simply not find details for the synthetic ID, which is acceptable

### File Changes Summary

1. **AiCodeGraph.Core/CallGraph/CallGraphBuilder.cs** - Add `TopLevelEntryPointId` constant, extend `WalkMethodBodies` to process `GlobalStatementSyntax` nodes and top-level local functions
2. **AiCodeGraph.Core/Storage/StorageService.cs** (optional safety) - Add synthetic ID exclusion to `GetDeadCodeAsync` SQL query

**Test Strategy:**

1. **Unit test for CallGraphBuilder with top-level statements:**
   - Create a test compilation with top-level statements calling a method (e.g., `MyClass.DoWork()`)
   - Verify `BuildCallGraph()` produces an edge from `TopLevelEntryPointId` to the callee method ID
   - Verify the callee no longer has zero callers

2. **Unit test for local functions in top-level statements:**
   - Create a test compilation with a local function defined in top-level scope that calls another method
   - Verify edges: `TopLevelEntryPointId` -> local function, local function -> callee

3. **Integration test with test fixture:**
   - Add a file with top-level statements to the test fixture that calls a method from another project/class
   - Run the full analyze pipeline
   - Query `GetDeadCodeAsync()` and verify the called method is NOT in the dead-code results

4. **Regression test for existing dead-code detection:**
   - Verify that genuinely uncalled private methods still appear in dead-code results
   - Verify that existing exclusions (constructors, Main, Dispose, test methods) still work

5. **End-to-end CLI test:**
   - Run `dead-code` command against a database built from a solution with top-level statements
   - Verify methods called from top-level statements are absent from output
   - Verify the synthetic entry point ID itself does not appear in output

6. **Callgraph command compatibility:**
   - Run `callgraph` on a method called from top-level statements
   - Verify the synthetic caller ID appears gracefully (or is labeled as entry point)
