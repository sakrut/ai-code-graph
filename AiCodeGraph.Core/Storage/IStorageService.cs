using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Models.CodeGraph;

namespace AiCodeGraph.Core.Storage;

public interface IStorageService : IAsyncDisposable, IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task OpenAsync(CancellationToken cancellationToken = default);

    // Write operations
    Task SaveCodeModelAsync(List<ExtractionResult> results, CancellationToken cancellationToken = default);
    Task SaveCallGraphAsync(List<(string CallerId, string CalleeId)> calls, CancellationToken cancellationToken = default);
    Task SaveMetricsAsync(List<(string MethodId, int CognitiveComplexity, int LinesOfCode, int NestingDepth)> metrics, CancellationToken cancellationToken = default);
    Task SaveEmbeddingsAsync(List<(string MethodId, float[] Vector, string ModelVersion)> embeddings, CancellationToken cancellationToken = default);
    Task SaveNormalizedMethodsAsync(List<(string MethodId, string StructuralSignature, string SemanticPayload)> normalized, CancellationToken cancellationToken = default);
    Task SaveClonePairsAsync(List<ClonePair> clonePairs, CancellationToken cancellationToken = default);
    Task SaveClustersAsync(List<IntentCluster> clusters, CancellationToken cancellationToken = default);
    Task SaveMetadataAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);

    // Read operations
    Task<List<(string Id, string Name, string FullName, string ReturnType, string? FilePath, int StartLine)>> GetHotspotsAsync(int top = 20, CancellationToken cancellationToken = default);
    Task<List<(string Id, string Name, string FullName, int Complexity, int Loc, int Nesting, string? FilePath, int StartLine)>> GetHotspotsWithThresholdAsync(int top = 20, int? threshold = null, CancellationToken cancellationToken = default);
    Task<List<string>> GetCalleesAsync(string methodId, CancellationToken cancellationToken = default);
    Task<List<string>> GetCallersAsync(string methodId, CancellationToken cancellationToken = default);
    Task<List<(string Id, string FullName)>> SearchMethodsAsync(string pattern, CancellationToken cancellationToken = default);
    Task<(string Id, string Name, string FullName, string? FilePath, int StartLine)?> GetMethodInfoAsync(string methodId, CancellationToken cancellationToken = default);
    Task<List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)>> GetTreeAsync(string? namespaceFilter = null, string? typeFilter = null, bool includePrivate = false, bool includeConstructors = false, bool skipTests = false, bool skipInterfaces = false, string? excludeNamespaces = null, CancellationToken cancellationToken = default);
    Task<List<(string MethodId, float[] Vector)>> GetEmbeddingsAsync(CancellationToken cancellationToken = default);
    Task<List<ClonePair>> GetClonePairsAsync(float minThreshold = 0f, CloneType? typeFilter = null, string? conceptFilter = null, CancellationToken cancellationToken = default);
    Task<List<IntentCluster>> GetClustersAsync(CancellationToken cancellationToken = default);
    Task<List<(string Id, string Name, string FullName, string ReturnType, string? FilePath, int StartLine, int Complexity, int Loc, int Nesting, string? ClusterLabel)>> GetMethodsForExportAsync(string? conceptFilter = null, CancellationToken cancellationToken = default);
    Task<List<(string CallerId, string CalleeId)>> GetCallGraphForMethodsAsync(HashSet<string> methodIds, CancellationToken cancellationToken = default);
    Task<(int CognitiveComplexity, int LinesOfCode, int NestingDepth)?> GetMethodMetricsAsync(string methodId, CancellationToken cancellationToken = default);
    Task<(string Label, int MemberCount, float Cohesion)?> GetMethodClusterAsync(string methodId, CancellationToken cancellationToken = default);
    Task<List<(string OtherMethodId, string OtherFullName, float HybridScore, CloneType Type)>> GetMethodDuplicatesAsync(string methodId, CancellationToken cancellationToken = default);
    Task<List<(string Id, string FullName, string? FilePath, int StartLine, int Complexity)>> GetDeadCodeAsync(bool includeOverrides = false, CancellationToken cancellationToken = default);
}
