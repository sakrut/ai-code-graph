# Task ID: 26

**Title:** Extract Analyze Command into Stage Methods

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Break the 175-line analyze command action in Program.cs into named static stage methods for readability and maintainability.

**Details:**

File: AiCodeGraph.Cli/Program.cs lines 46-220

Extract into these static methods (keep in Program.cs per constraint):

```csharp
private static async Task<LoadedWorkspace> LoadWorkspaceStage(string solutionPath, bool verbose)
{
    // Lines ~55-85: MSBuild locator, workspace opening, solution loading
}

private static List<ExtractionResult> ExtractCodeModelStage(LoadedWorkspace workspace, bool verbose)
{
    // Lines ~87-100: CodeModelExtractor usage
}

private static List<(string, string)> BuildCallGraphStage(LoadedWorkspace workspace, List<ExtractionResult> results, bool verbose)
{
    // Lines ~102-115: CallGraphBuilder usage
}

private static List<(string, int, int, int)> ComputeMetricsStage(LoadedWorkspace workspace, List<ExtractionResult> results, bool verbose)
{
    // Lines ~117-130: MetricsEngine usage
}

private static List<NormalizedMethod> NormalizeMethodsStage(LoadedWorkspace workspace, List<ExtractionResult> results, bool verbose)
{
    // Lines ~132-145: IntentNormalizer usage
}

private static List<(string, float[], string)> GenerateEmbeddingsStage(List<NormalizedMethod> normalized, bool verbose)
{
    // Lines ~147-160: HashEmbeddingEngine usage
}

private static async Task StoreResultsStage(StorageService storage, ...all data..., CancellationToken ct)
{
    // Lines ~162-180: All Save* calls
}

private static async Task DetectDuplicatesStage(StorageService storage, ...params..., bool verbose, CancellationToken ct)
{
    // Lines ~182-200: Clone detection + clustering
}

private static async Task SaveBaselineStage(string dbPath, bool saveBaseline)
{
    // Lines ~202-210: Copy DB to baseline path
}
```

The analyze command action becomes a clean orchestration of these stages with progress reporting between each.

**Test Strategy:**

All existing IntegrationTests must pass unchanged. The analyze command should produce identical output and database content. Run full test suite with `dotnet test` to verify no regressions.

## Subtasks

### 26.1. Extract LoadWorkspaceStage and ExtractCodeModelStage methods

**Status:** done  
**Dependencies:** None  

Define the first two stage method signatures and extract the workspace loading logic (lines 57-80) and code model extraction logic (lines 83-98) into dedicated private static methods in Program.cs.

**Details:**

Create `private static async Task<(LoadedWorkspace workspace, string resolvedPath)> LoadWorkspaceStage(string? solutionPath, bool verbose, CancellationToken ct)` extracting SolutionDiscovery.FindSolutionFile, WorkspaceLoader instantiation, progress reporting, LoadSolutionAsync call, and diagnostic output. Create `private static List<ExtractionResult> ExtractCodeModelStage(LoadedWorkspace workspace, bool verbose)` extracting the CodeModelExtractor loop over compilations. Both methods should include their own Stopwatch timing and console output. The LoadWorkspaceStage needs to return the resolved path since it's used later for display, and must properly handle the using/dispose pattern for WorkspaceLoader (consider returning it as IDisposable or letting the caller manage disposal).

### 26.2. Extract BuildCallGraphStage, ComputeMetricsStage, and NormalizeMethodsStage

**Status:** done  
**Dependencies:** 26.1  

Extract the call graph building (lines 101-106), metrics computation (lines 108-113), and method normalization (lines 115-120) into three private static methods.

**Details:**

Create `private static List<CallEdge> BuildCallGraphStage(LoadedWorkspace workspace, bool verbose)` wrapping CallGraphBuilder instantiation and BuildCallGraph call. Create `private static List<MethodMetrics> ComputeMetricsStage(LoadedWorkspace workspace, bool verbose)` wrapping MetricsEngine instantiation and ComputeMetrics call. Create `private static List<NormalizedMethod> NormalizeMethodsStage(LoadedWorkspace workspace, bool verbose)` wrapping IntentNormalizer instantiation and NormalizeAll call. Each method includes its own timing Stopwatch and console write for the stage progress. Use the actual return types from the Core library (CallEdge, MethodMetrics, NormalizedMethod) rather than tuples where possible to maintain type safety.

### 26.3. Extract GenerateEmbeddingsStage, StoreResultsStage, DetectDuplicatesStage, and SaveBaselineStage

**Status:** done  
**Dependencies:** 26.1, 26.2  

Extract the remaining four stages: embedding generation (lines 122-129), storage (lines 131-150), duplicate detection/clustering (lines 152-168), and baseline saving (lines 170-176) into private static methods.

**Details:**

Create `private static List<(string MethodId, float[] Vector, string Model)> GenerateEmbeddingsStage(List<NormalizedMethod> normalized, bool verbose)` wrapping HashEmbeddingEngine usage. Create `private static async Task StoreResultsStage(string output, List<ExtractionResult> extractionResults, List<CallEdge> edges, List<MethodMetrics> metrics, List<NormalizedMethod> normalized, List<(string, float[], string)> embeddings, CancellationToken ct)` that returns the StorageService instance (or dbPath) for subsequent use - handles Directory.CreateDirectory, StorageService init, and all Save* calls. Create `private static async Task<(List<ClonePair> clonePairs, List<MethodCluster> clusters)> DetectDuplicatesStage(StorageService storage, List<NormalizedMethod> normalized, List<(string, float[])> embeddingPairs, bool verbose, CancellationToken ct)` handling structural/semantic clone detection, hybrid scoring, and clustering. Create `private static void SaveBaselineStage(string dbPath, string output, bool saveBaseline)` for the conditional file copy. Consider disposal patterns for StorageService and HashEmbeddingEngine carefully.

### 26.4. Refactor analyze command action to orchestrate stages and verify tests

**Status:** done  
**Dependencies:** 26.1, 26.2, 26.3  

Replace the monolithic analyze command action body with sequential calls to the extracted stage methods, keeping only orchestration logic, option parsing, error handling, and the summary output in the action lambda.

**Details:**

Rewrite the analyzeCommand.SetAction lambda (lines 46-220) to: (1) parse options at the top, (2) call each stage method in sequence with appropriate data passing between them, (3) keep the try/catch error handling wrapping all stage calls, (4) keep the summary console output at the end using return values from stages. The lambda should read as a clear pipeline: LoadWorkspace → ExtractCodeModel → BuildCallGraph → ComputeMetrics → NormalizeMethods → GenerateEmbeddings → StoreResults → DetectDuplicates → SaveBaseline → PrintSummary. Ensure the totalTimer Stopwatch remains in the orchestrator. The orchestrator should be roughly 40-50 lines maximum. Verify resource disposal (WorkspaceLoader, StorageService, HashEmbeddingEngine) is handled correctly across the stage boundaries - consider whether using statements need to stay in the orchestrator or move into stages.
