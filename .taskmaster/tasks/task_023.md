# Task ID: 23

**Title:** DriftDetector Connection Leak Fix

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Fix potential connection leak in DriftDetector.CompareAsync where if one connection's OpenAsync throws, the other may not be disposed properly.

**Details:**

File: AiCodeGraph.Core/Drift/DriftDetector.cs lines 28-32

Current code uses `await using` declarations which should handle disposal, but if the first OpenAsync succeeds and the second throws, the first connection needs explicit cleanup. The fix depends on actual disposal semantics:

Option A - Restructure with try-finally (if current code doesn't properly dispose on exception):
```csharp
public async Task<DriftReport> CompareAsync(string currentDbPath, string baselineDbPath, CancellationToken ct)
{
    SqliteConnection? currentConn = null;
    SqliteConnection? baselineConn = null;
    try
    {
        currentConn = new SqliteConnection($"Data Source={currentDbPath}");
        baselineConn = new SqliteConnection($"Data Source={baselineDbPath}");
        
        await currentConn.OpenAsync(ct).ConfigureAwait(false);
        await baselineConn.OpenAsync(ct).ConfigureAwait(false);
        
        // ... rest of comparison logic ...
    }
    finally
    {
        if (baselineConn != null) await baselineConn.DisposeAsync();
        if (currentConn != null) await currentConn.DisposeAsync();
    }
}
```

Option B - If `await using` declarations already ensure disposal correctly with C# semantics (they should in reverse declaration order), add file existence validation before opening:
```csharp
if (!File.Exists(currentDbPath))
    throw new FileNotFoundException("Current database not found", currentDbPath);
if (!File.Exists(baselineDbPath))
    throw new FileNotFoundException("Baseline database not found", baselineDbPath);
```

Prefer Option A for explicit safety.

**Test Strategy:**

Add tests in DriftDetectorTests.cs: (1) Test with invalid/non-existent baseline path - verify no leaked connections (use a counter or wrapper). (2) Test with invalid current path. (3) Test with both invalid. (4) Test normal operation still works. Verify using process file handle count or connection tracking.

## Subtasks

### 23.1. Restructure CompareAsync with explicit try-finally for connection lifecycle

**Status:** pending  
**Dependencies:** None  

Replace the `await using` declaration pattern at lines 28-32 with explicit try-finally blocks to guarantee both SqliteConnections are disposed even if OpenAsync throws on the second connection.

**Details:**

In DriftDetector.cs, refactor CompareAsync to declare `SqliteConnection? currentConn = null;` and `SqliteConnection? baselineConn = null;` before a try block. Inside the try block, instantiate both connections and call OpenAsync on each with ConfigureAwait(false). In the finally block, dispose both connections using `if (conn != null) await conn.DisposeAsync().ConfigureAwait(false);` in reverse order (baselineConn first, then currentConn). Move all the DetectNewMethods/DetectRemovedMethods/DetectComplexityRegressions/DetectNewDuplicates/DetectIntentScattering calls inside the try block after both connections are opened. Keep the existing File.Exists checks (lines 23-26) before the try block as early-exit validation.

### 23.2. Add ConfigureAwait(false) to all async calls in DriftDetector

**Status:** pending  
**Dependencies:** 23.1  

Add ConfigureAwait(false) to all await calls in DriftDetector.cs to avoid potential deadlocks when called from synchronous contexts and to follow .NET library best practices.

**Details:**

After the try-finally restructuring, append `.ConfigureAwait(false)` to every `await` expression in DriftDetector.cs. This includes: both OpenAsync calls, all five Detect* method calls in CompareAsync, and all await calls in the private helper methods (GetMethodIds, GetMethodDetails, GetMetrics, GetMethodFullName, GetClonePairKeys, GetClonePairs, GetClusterNamespaces, TableExists). Each ExecuteReaderAsync, ReadAsync, ExecuteScalarAsync, and the internal DetectNewMethods/etc. calls should have ConfigureAwait(false). This is a systematic change across approximately 25-30 await expressions in the file.

### 23.3. Add test for connection disposal when second OpenAsync fails

**Status:** pending  
**Dependencies:** 23.1  

Add a test that verifies no connection leak occurs when the baseline database path points to an invalid/corrupt SQLite file that causes OpenAsync to throw after the current connection has already been opened.

**Details:**

In DriftDetectorTests.cs, add a test method `Compare_SecondOpenFails_DisposesFirstConnection`. Create a valid current database using the existing CreateDatabase helper. For the baseline path, create a file with invalid content (e.g., write random bytes to simulate a corrupt SQLite file that passes File.Exists but fails on OpenAsync). Call CompareAsync and assert it throws SqliteException. After the exception, verify the current database file is not locked by attempting to open it with a new SqliteConnection (confirming the first connection was properly disposed). Use a try-catch pattern since we expect the exception.

### 23.4. Add test for CancellationToken support in CompareAsync

**Status:** pending  
**Dependencies:** 23.1  

Add a test verifying that CompareAsync respects CancellationToken cancellation and properly disposes connections when cancelled mid-operation.

**Details:**

In DriftDetectorTests.cs, add a test method `Compare_CancelledToken_ThrowsAndDisposesConnections`. Create two valid databases with methods data using the existing CreateDatabase helper. Create a pre-cancelled CancellationTokenSource (`new CancellationTokenSource()` then call `Cancel()`). Call CompareAsync with the cancelled token. Assert it throws OperationCanceledException or TaskCanceledException. After the exception, verify both database files can be opened with new connections (confirming both were disposed). This validates that the try-finally correctly handles cancellation exceptions thrown during OpenAsync.

### 23.5. Add test for missing current database path throws FileNotFoundException

**Status:** pending  
**Dependencies:** 23.1  

Add a test complementing the existing Compare_MissingBaseline_Throws test to verify that a non-existent current database path also throws FileNotFoundException before any connections are created.

**Details:**

In DriftDetectorTests.cs, add a test method `Compare_MissingCurrent_Throws`. Create a valid baseline database using CreateDatabase helper. Call CompareAsync with a non-existent path for currentDbPath (e.g., '/nonexistent/current.db') and the valid baseline path. Assert it throws FileNotFoundException with the correct FileName property matching the non-existent path. This validates the early File.Exists validation at lines 23-24 prevents connection creation for invalid paths. Also add `Compare_BothMissing_ThrowsForCurrent` to verify the first check (currentDbPath) fires first when both paths are invalid.
