# Task ID: 27

**Title:** Add IStorageService Interface

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Extract IStorageService interface from StorageService covering all public methods, enabling DI registration and test mocking.

**Details:**

Create new file: AiCodeGraph.Core/Storage/IStorageService.cs

```csharp
using AiCodeGraph.Core.Duplicates;

namespace AiCodeGraph.Core.Storage;

public interface IStorageService : IAsyncDisposable, IDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task OpenAsync(CancellationToken ct = default);
    
    // Write operations
    Task SaveCodeModelAsync(List<ExtractionResult> results, CancellationToken ct = default);
    Task SaveCallGraphAsync(List<(string CallerId, string CalleeId)> calls, CancellationToken ct = default);
    Task SaveMetricsAsync(List<(string MethodId, int CognitiveComplexity, int LOC, int NestingDepth)> metrics, CancellationToken ct = default);
    Task SaveEmbeddingsAsync(List<(string MethodId, float[] Vector, string ModelVersion)> embeddings, CancellationToken ct = default);
    Task SaveNormalizedMethodsAsync(List<(string MethodId, string StructuralSignature, string SemanticPayload)> normalized, CancellationToken ct = default);
    Task SaveClonePairsAsync(List<ClonePair> clonePairs, CancellationToken ct = default);
    Task SaveClustersAsync(List<IntentCluster> clusters, CancellationToken ct = default);
    
    // Read operations
    Task<List<(string, string, string, string, string?, int)>> GetHotspotsAsync(int top = 20, CancellationToken ct = default);
    Task<List<(string, string, string, int, int, int, string?, int)>> GetHotspotsWithThresholdAsync(int top = 20, int? threshold = null, CancellationToken ct = default);
    Task<List<string>> GetCalleesAsync(string methodId, CancellationToken ct = default);
    Task<List<string>> GetCallersAsync(string methodId, CancellationToken ct = default);
    Task<List<(string, string)>> SearchMethodsAsync(string pattern, CancellationToken ct = default);
    Task<(string Id, string Name, string FullName, string? FilePath, int StartLine)?> GetMethodInfoAsync(string methodId, CancellationToken ct = default);
    Task<List<(string, string, string, string, string, string)>> GetTreeAsync(string? ns = null, string? type = null, CancellationToken ct = default);
    Task<List<(string MethodId, float[] Vector)>> GetEmbeddingsAsync(CancellationToken ct = default);
    Task<List<ClonePair>> GetClonePairsAsync(float minThreshold = 0f, CloneType? typeFilter = null, string? conceptFilter = null, CancellationToken ct = default);
    Task<List<IntentCluster>> GetClustersAsync(CancellationToken ct = default);
    Task<List<(string, string, string, string, string, int, int, int, int, string?)>> GetMethodsForExportAsync(string? conceptFilter = null, CancellationToken ct = default);
    Task<List<(string CallerId, string CalleeId)>> GetCallGraphForMethodsAsync(HashSet<string> methodIds, CancellationToken ct = default);
    Task<(int CognitiveComplexity, int LinesOfCode, int NestingDepth)?> GetMethodMetricsAsync(string methodId, CancellationToken ct = default);
    Task<(string Label, int MemberCount, float Cohesion)?> GetMethodClusterAsync(string methodId, CancellationToken ct = default);
    Task<List<(string OtherMethodId, string OtherFullName, float HybridScore, CloneType Type)>> GetMethodDuplicatesAsync(string methodId, CancellationToken ct = default);
}
```

Modify StorageService.cs to implement the interface:
```csharp
public class StorageService : IStorageService
```

No other changes needed at this stage - consumers continue using StorageService directly until DI wiring in Phase 3.

**Test Strategy:**

Verify StorageService compiles with IStorageService implementation. Add a compile-time test that casts StorageService to IStorageService. All existing StorageServiceTests pass. Verify no missing methods by attempting `IStorageService svc = new StorageService()` in a test.

## Subtasks

### 27.1. Create IStorageService.cs with all public method signatures

**Status:** pending  
**Dependencies:** None  

Create the IStorageService interface file in AiCodeGraph.Core/Storage/ containing all 20+ public method signatures extracted from StorageService, including proper using directives, tuple return types, nullable annotations, and default parameter values.

**Details:**

Create file AiCodeGraph.Core/Storage/IStorageService.cs. The interface must extend IAsyncDisposable and IDisposable. Extract every public method signature from StorageService.cs (845 lines), preserving exact return types including complex tuples like Task<List<(string, string, string, string, string?, int)>>, nullable return types, CancellationToken defaults, and optional parameters with defaults (e.g., int top = 20, float minThreshold = 0f). Include required using directives: AiCodeGraph.Core.Duplicates for ClonePair/IntentCluster/CloneType, and Microsoft.CodeAnalysis types if needed. Group methods with comments for Write operations and Read operations. Verify all 20+ method signatures are present by cross-referencing with StorageService.cs public methods.

### 27.2. Modify StorageService to implement IStorageService

**Status:** pending  
**Dependencies:** 27.1  

Update the StorageService class declaration to explicitly implement the IStorageService interface and verify the solution compiles successfully with no missing method implementations.

**Details:**

In AiCodeGraph.Core/Storage/StorageService.cs, change the class declaration from 'public class StorageService' (or whatever it currently extends) to 'public class StorageService : IStorageService'. Ensure the class already implements IAsyncDisposable and IDisposable (which the interface requires). Run `dotnet build` to confirm compilation succeeds - any missing method implementations or signature mismatches will surface as compiler errors. Fix any discrepancies between the interface signatures and the actual StorageService method signatures. No other consumers need changing at this stage.

### 27.3. Add compile-time verification test and run existing tests

**Status:** pending  
**Dependencies:** 27.2  

Add a test that verifies StorageService can be assigned to IStorageService (compile-time contract check) and ensure all existing StorageServiceTests continue to pass without modification.

**Details:**

In AiCodeGraph.Tests, add a test method (in an existing or new test file like StorageServiceInterfaceTests.cs) that performs: IStorageService svc = new StorageService("test.db"); This compile-time cast verifies the contract is fully satisfied. The test itself can simply assert svc is not null and then dispose it. Run the full test suite with `dotnet test` to confirm all existing StorageServiceTests (and other tests) pass unchanged. This validates that adding the interface declaration did not alter any behavior.
