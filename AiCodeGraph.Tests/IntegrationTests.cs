using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Tests;

public class IntegrationTests : TempDirectoryFixture
{
    private readonly string _dbPath;
    private readonly string _fixturePath;

    public IntegrationTests() : base("integration-test")
    {
        _dbPath = GetDbPath();

        // Find fixture path relative to test assembly location
        var assemblyDir = Path.GetDirectoryName(typeof(IntegrationTests).Assembly.Location)!;
        _fixturePath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "tests", "fixtures", "TestSolution", "TestSolution.sln"));
    }

    [Fact]
    public async Task FullPipeline_LoadsAndAnalyzesSolution()
    {
        if (!File.Exists(_fixturePath))
            return;

        // 1. Load workspace
        LoadedWorkspace workspace;
        try
        {
            using var loader = new WorkspaceLoader();
            workspace = await loader.LoadSolutionAsync(_fixturePath);
        }
        catch (FileNotFoundException)
        {
            return; // MSBuild not available in test environment
        }
        Assert.True(workspace.Compilations.Count >= 1);

        // 2. Extract code model
        var extractor = new CodeModelExtractor();
        var results = new List<ExtractionResult>();
        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            var project = workspace.Solution.GetProject(projectId);
            var name = project?.Name ?? projectId.Id.ToString();
            var filePath = project?.FilePath ?? "";
            results.Add(extractor.ExtractProject(compilation, name, filePath));
        }

        var totalMethods = results.Sum(r => CountMethods(r.Model));
        Assert.True(totalMethods >= 50, $"Expected >= 50 methods, got {totalMethods}");

        // 3. Build call graph
        var callGraphBuilder = new CallGraphBuilder();
        var edges = callGraphBuilder.BuildCallGraph(workspace);
        Assert.True(edges.Count > 0, "Expected call graph edges");

        // 4. Compute metrics
        var metricsEngine = new MetricsEngine();
        var metrics = metricsEngine.ComputeMetrics(workspace);
        Assert.True(metrics.Count > 0, "Expected metrics");

        // 5. Normalize
        var normalizer = new IntentNormalizer();
        var normalized = normalizer.NormalizeAll(workspace);
        Assert.True(normalized.Count > 0, "Expected normalized methods");

        // 6. Generate embeddings
        using var embeddingEngine = new HashEmbeddingEngine();
        var embeddingResults = normalized
            .Select(n => (n.MethodId, Vector: embeddingEngine.GenerateEmbedding(n.SemanticPayload), Model: "hash-v1"))
            .ToList();

        // 7. Detect duplicates
        var structuralDetector = new StructuralCloneDetector();
        var semanticDetector = new SemanticCloneDetector();
        var hybridScorer = new HybridScorer();

        var structuralClones = structuralDetector.DetectClones(normalized);
        var embeddingPairs = embeddingResults.Select(e => (e.MethodId, e.Vector)).ToList();
        var semanticClones = semanticDetector.DetectClones(embeddingPairs, threshold: 0.7f);
        var clonePairs = hybridScorer.Merge(structuralClones, semanticClones);

        // 8. Store everything
        await using var storage = new StorageService(_dbPath);
        await storage.InitializeAsync();
        await storage.SaveCodeModelAsync(results);
        await storage.SaveCallGraphAsync(edges.Select(e => (e.CallerId, e.CalleeId)).ToList());
        await storage.SaveMetricsAsync(metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList());
        await storage.SaveNormalizedMethodsAsync(normalized.Select(n => (n.MethodId, n.StructuralSignature, n.SemanticPayload)).ToList());
        await storage.SaveEmbeddingsAsync(embeddingResults);
        await storage.SaveClonePairsAsync(clonePairs);

        // 9. Verify database content
        var hotspots = await storage.GetHotspotsWithThresholdAsync(100);
        Assert.True(hotspots.Count > 0, "Expected hotspots in database");

        // Complex methods (ProcessUserBatchAsync, ProcessOrderBatchAsync, etc.) should have high complexity
        var highComplexity = hotspots.Where(h => h.Complexity >= 5).ToList();
        Assert.True(highComplexity.Count > 0, "Expected high-complexity methods");

        // 10. Verify search works
        var allEmbeddings = await storage.GetEmbeddingsAsync();
        Assert.True(allEmbeddings.Count > 0);

        var queryVector = embeddingEngine.GenerateEmbedding("create user");
        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var searchResults = index.Search(queryVector, 5);
        Assert.True(searchResults.Count > 0, "Expected search results");
    }

    [Fact]
    public async Task FullPipeline_OutputIsDeterministic()
    {
        if (!File.Exists(_fixturePath))
            return;

        var dbPath1 = Path.Combine(TempDir, "run1.db");
        var dbPath2 = Path.Combine(TempDir, "run2.db");

        await RunAnalysis(dbPath1);
        await RunAnalysis(dbPath2);

        // If MSBuild wasn't available, RunAnalysis returned early without creating DBs
        if (!File.Exists(dbPath1))
            return;

        // Compare method counts
        await using var s1 = new StorageService(dbPath1);
        await s1.OpenAsync();
        await using var s2 = new StorageService(dbPath2);
        await s2.OpenAsync();

        var methods1 = await s1.SearchMethodsAsync("");
        var methods2 = await s2.SearchMethodsAsync("");
        Assert.Equal(methods1.Count, methods2.Count);

        var hotspots1 = await s1.GetHotspotsWithThresholdAsync(1000);
        var hotspots2 = await s2.GetHotspotsWithThresholdAsync(1000);
        Assert.Equal(hotspots1.Count, hotspots2.Count);

        // Compare individual metrics
        for (int i = 0; i < hotspots1.Count; i++)
        {
            Assert.Equal(hotspots1[i].Id, hotspots2[i].Id);
            Assert.Equal(hotspots1[i].Complexity, hotspots2[i].Complexity);
        }
    }

    private async Task RunAnalysis(string dbPath)
    {
        LoadedWorkspace workspace;
        try
        {
            using var loader = new WorkspaceLoader();
            workspace = await loader.LoadSolutionAsync(_fixturePath);
        }
        catch (FileNotFoundException) { return; }

        var extractor = new CodeModelExtractor();
        var results = new List<ExtractionResult>();
        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            var project = workspace.Solution.GetProject(projectId);
            results.Add(extractor.ExtractProject(compilation, project?.Name ?? "", project?.FilePath ?? ""));
        }

        var callGraphBuilder = new CallGraphBuilder();
        var edges = callGraphBuilder.BuildCallGraph(workspace);

        var metricsEngine = new MetricsEngine();
        var metrics = metricsEngine.ComputeMetrics(workspace);

        await using var storage = new StorageService(dbPath);
        await storage.InitializeAsync();
        await storage.SaveCodeModelAsync(results);
        await storage.SaveCallGraphAsync(edges.Select(e => (e.CallerId, e.CalleeId)).ToList());
        await storage.SaveMetricsAsync(metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList());
    }

    private static int CountMethods(ProjectModel project)
    {
        return project.Namespaces.Sum(ns => CountMethodsInNs(ns));
    }

    private static int CountMethodsInNs(NamespaceModel ns)
    {
        return ns.Types.Sum(t => t.Methods.Count + t.NestedTypes.Sum(TestHelpers.CountMethodsInType))
            + ns.ChildNamespaces.Sum(c => CountMethodsInNs(c));
    }

}
