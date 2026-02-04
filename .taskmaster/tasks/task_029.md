# Task ID: 29

**Title:** Add ConfigureAwait(false) to Core Library Async Methods

**Status:** done

**Dependencies:** None

**Priority:** low

**Description:** Add ConfigureAwait(false) to all await calls in AiCodeGraph.Core to prevent unnecessary synchronization context capture in library code.

**Details:**

Files: All async methods in AiCodeGraph.Core/ (StorageService.cs, DriftDetector.cs, and any other async methods)

Constraint: Do NOT add to CLI/Program.cs (top-level code needs sync context for console output).

Pattern to apply:
```csharp
// Before:
await connection.OpenAsync(ct);
var reader = await cmd.ExecuteReaderAsync(ct);

// After:
await connection.OpenAsync(ct).ConfigureAwait(false);
var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
```

Files to modify:
1. `AiCodeGraph.Core/Storage/StorageService.cs` - All async methods (InitializeAsync, OpenAsync, Save*, Get*, Search*)
2. `AiCodeGraph.Core/Drift/DriftDetector.cs` - CompareAsync and helper methods

Use search-and-replace carefully. Every `await` in Core that doesn't already have ConfigureAwait should get it.

Do NOT modify:
- AiCodeGraph.Cli/Program.cs
- AiCodeGraph.Tests/ (test context may matter)

**Test Strategy:**

Run full test suite with `dotnet test` - all 178 existing tests must pass. Behavior should be completely unchanged since these are library methods that don't depend on synchronization context.

## Subtasks

### 29.1. Add ConfigureAwait(false) to all await calls in StorageService.cs

**Status:** pending  
**Dependencies:** None  

Add .ConfigureAwait(false) to every await expression in AiCodeGraph.Core/Storage/StorageService.cs. This file has ~40+ await calls across 20+ async methods (InitializeAsync, OpenAsync, SaveMethodAsync, GetMethodsAsync, SearchAsync, etc.). Each await that does not already have ConfigureAwait must be appended with .ConfigureAwait(false).

**Details:**

Systematically go through StorageService.cs and apply the pattern to every await call:

1. Search for all `await` expressions in the file
2. For each await, append `.ConfigureAwait(false)` before the semicolon
3. Handle different await patterns:
   - Simple: `await x.MethodAsync(args);` → `await x.MethodAsync(args).ConfigureAwait(false);`
   - With assignment: `var result = await x.MethodAsync(args);` → `var result = await x.MethodAsync(args).ConfigureAwait(false);`
   - With cast/property access: `await (await x.MethodAsync()).OtherAsync();` → handle each await separately
   - In using statements: `await using var x = ...` → these may not need ConfigureAwait
4. Do NOT modify any file outside AiCodeGraph.Core/
5. Verify the file still compiles with `dotnet build AiCodeGraph.Core`

Expected volume: ~40-50 await calls across methods like InitializeAsync, OpenAsync, SaveMethodAsync, SaveCallGraphAsync, GetMethodsAsync, GetCallGraphAsync, SearchMethodsAsync, etc.

### 29.2. Add ConfigureAwait(false) to DriftDetector.cs and verify full test suite

**Status:** pending  
**Dependencies:** 29.1  

Add .ConfigureAwait(false) to every await expression in AiCodeGraph.Core/Drift/DriftDetector.cs (~20+ await calls in CompareAsync and helper methods), then run the full test suite to verify all changes across both files are correct.

**Details:**

1. Search for all `await` expressions in DriftDetector.cs
2. For each await, append `.ConfigureAwait(false)` before the semicolon, following the same patterns as StorageService.cs:
   - Simple awaits: `await x.MethodAsync(ct);` → `await x.MethodAsync(ct).ConfigureAwait(false);`
   - Assignment awaits: `var result = await x.MethodAsync(ct);` → `var result = await x.MethodAsync(ct).ConfigureAwait(false);`
3. Check for any other async files in AiCodeGraph.Core/ that may have been missed (e.g., any async methods in CallGraphBuilder, WorkspaceLoader, or other classes)
4. Do NOT modify AiCodeGraph.Cli/Program.cs or any files in AiCodeGraph.Tests/
5. Run `dotnet build` to verify full solution compiles
6. Run `dotnet test` to verify all 178 tests pass
7. Do a final grep across AiCodeGraph.Core/ to confirm no await calls remain without ConfigureAwait(false)

Expected volume in DriftDetector.cs: ~20-25 await calls in CompareAsync, LoadMethodsAsync, LoadCallGraphAsync, and other helper methods.
