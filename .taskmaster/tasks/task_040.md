# Task ID: 40

**Title:** Error Path Tests for Core Classes

**Status:** done

**Dependencies:** 24 ✓

**Priority:** medium

**Description:** Add tests for error conditions across core classes: StorageService with null/invalid paths, VectorIndex with empty data, DriftDetector with missing files, IntentClusterer with empty input.

**Details:**

Create new file: AiCodeGraph.Tests/ErrorPathTests.cs

```csharp
namespace AiCodeGraph.Tests;

public class ErrorPathTests
{
    // StorageService error paths
    [Fact]
    public async Task StorageService_NullDbPath_UsesDefault() { /* ... */ }
    
    [Fact]
    public async Task StorageService_InvalidPath_ThrowsOnOpen() { /* ... */ }
    
    [Fact]
    public async Task StorageService_OpenBeforeInit_Throws() { /* ... */ }
    
    // VectorIndex error paths (depends on task 24 validation)
    [Fact]
    public void VectorIndex_SearchEmptyIndex_ReturnsEmpty() { /* ... */ }
    
    [Fact]
    public void VectorIndex_AddNullVector_ThrowsArgumentNull() { /* ... */ }
    
    [Fact]
    public void VectorIndex_AddNaNVector_ThrowsArgument() { /* ... */ }
    
    [Fact]
    public void VectorIndex_MismatchedDimensions_ThrowsArgument() { /* ... */ }
    
    // DriftDetector error paths
    [Fact]
    public async Task DriftDetector_MissingBaselineFile_ThrowsFileNotFound() { /* ... */ }
    
    [Fact]
    public async Task DriftDetector_MissingCurrentFile_ThrowsFileNotFound() { /* ... */ }
    
    [Fact]
    public async Task DriftDetector_EmptyDatabase_ReturnsEmptyReport() { /* ... */ }
    
    // IntentClusterer error paths
    [Fact]
    public void IntentClusterer_EmptyMethodList_ReturnsEmptyClusters() { /* ... */ }
    
    [Fact]
    public void IntentClusterer_FewerThanMinPoints_ReturnsEmptyClusters() { /* ... */ }
    
    [Fact]
    public void IntentClusterer_NullEmbeddings_HandlesGracefully() { /* ... */ }
}
```

**Test Strategy:**

Each test verifies specific error behavior: correct exception types, graceful handling of edge cases, and proper error messages. Use Assert.Throws<T> for expected exceptions. Verify no resource leaks in error paths. Run with dotnet test to confirm all pass.

## Subtasks

### 40.1. Write StorageService error path tests

**Status:** pending  
**Dependencies:** None  

Implement tests for StorageService error conditions: calling methods before InitializeAsync throws InvalidOperationException, InitializeAsync/OpenAsync with invalid directory paths throws appropriate exceptions, and null dbPath uses default path.

**Details:**

Create AiCodeGraph.Tests/ErrorPathTests.cs with the StorageService section. Test EnsureConnection() throwing InvalidOperationException('Storage not initialized. Call InitializeAsync first.') when methods are called before InitializeAsync. Test that InitializeAsync with an invalid path (e.g., containing invalid characters or pointing to a read-only location) throws SqliteException or IOException. Test that OpenAsync on a non-existent database throws. Test that passing null to the constructor uses the default ':memory:' or generates a valid path. Use in-memory databases where possible to avoid filesystem side effects.

### 40.2. Write VectorIndex error path tests

**Status:** pending  
**Dependencies:** 40.1  

Implement tests for VectorIndex error conditions: searching an empty index returns empty results, adding vectors with mismatched dimensions throws ArgumentException, and BuildIndex with inconsistent vector sizes throws ArgumentException.

**Details:**

Add VectorIndex error path tests to ErrorPathTests.cs. Test Search() on a freshly constructed VectorIndex returns an empty list (line 39-40 early return). Test BuildIndex() with vectors of different dimensions throws ArgumentException (line 21 check). Test AddItem() after BuildIndex with a vector of wrong dimension throws ArgumentException (line 29 check). Note: null/NaN vector validation tests depend on task 24 adding input validation to VectorIndex - add placeholder comments or conditional tests for those. Test SaveToDisk with an invalid path throws IOException. Test LoadFromDisk with a non-existent file throws FileNotFoundException. Test LoadFromDisk with corrupted magic bytes throws InvalidDataException (line 87).

### 40.3. Write DriftDetector and IntentClusterer error path tests

**Status:** pending  
**Dependencies:** 40.1  

Implement tests for DriftDetector error conditions (missing current file, missing baseline file, empty databases) and IntentClusterer error conditions (empty method list, fewer than minPoints embeddings, null/empty embeddings).

**Details:**

Add DriftDetector tests to ErrorPathTests.cs: Test CompareAsync throws FileNotFoundException when currentDbPath doesn't exist (line 23). Test CompareAsync throws FileNotFoundException when baselineDbPath doesn't exist (line 25). Test CompareAsync with valid but empty databases returns a DriftReport with empty/zero-change collections. Use temp files with initialized but empty StorageService databases for the empty DB test. Add IntentClusterer tests: Test ClusterMethods with empty embeddings list (Count < minPoints) returns empty cluster list (line 20 early return). Test ClusterMethods with embeddings count less than minPoints (e.g., 2 embeddings with default minPoints=3) returns empty clusters. Test ClusterMethods with null or empty methods list handles gracefully - either returns empty or throws ArgumentNullException depending on implementation. Clean up all temp files in test teardown.
