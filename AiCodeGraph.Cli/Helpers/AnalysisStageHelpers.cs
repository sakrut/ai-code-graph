using System.Diagnostics;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Helper methods for the various stages of the analyze command.
/// </summary>
public static class AnalysisStageHelpers
{
    public static async Task<LoadedWorkspace> LoadWorkspaceStage(string resolvedPath, bool verbose, CancellationToken ct)
    {
        Console.Write("Loading workspace...");
        var timer = Stopwatch.StartNew();
        using var loader = new WorkspaceLoader();

        var progress = verbose
            ? new Progress<WorkspaceLoadProgress>(p =>
            {
                if (p.ProjectName != null)
                    Console.WriteLine($"  [{p.CurrentProject}/{p.TotalProjects}] {p.Phase}: {p.ProjectName}");
            })
            : null;

        var workspace = await loader.LoadSolutionAsync(resolvedPath, progress, ct);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");

        if (workspace.HasErrors)
        {
            foreach (var diag in workspace.Diagnostics)
                Console.Error.WriteLine($"  Warning: [{diag.Kind}] {diag.Message}");
        }

        return workspace;
    }

    public static List<ExtractionResult> ExtractCodeModelStage(LoadedWorkspace workspace, bool verbose)
    {
        Console.Write("Extracting code model...");
        var timer = Stopwatch.StartNew();
        var extractor = new CodeModelExtractor();
        var results = new List<ExtractionResult>();

        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            var project = workspace.Solution.GetProject(projectId);
            var name = project?.Name ?? projectId.Id.ToString();
            var filePath = project?.FilePath ?? "";
            var result = extractor.ExtractProject(compilation, name, filePath);
            results.Add(result);

            if (verbose)
                Console.WriteLine($"  {name}: {ModelCountHelpers.CountMethods(result.Model)} methods");
        }
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return results;
    }

    public static List<MethodCallEdge> BuildCallGraphStage(LoadedWorkspace workspace)
    {
        Console.Write("Building call graph...");
        var timer = Stopwatch.StartNew();
        var builder = new CallGraphBuilder();
        var edges = builder.BuildCallGraph(workspace);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return edges;
    }

    public static List<MethodMetrics> ComputeMetricsStage(LoadedWorkspace workspace)
    {
        Console.Write("Computing metrics...");
        var timer = Stopwatch.StartNew();
        var engine = new MetricsEngine();
        var metrics = engine.ComputeMetrics(workspace);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return metrics;
    }

    public static List<NormalizedMethod> NormalizeMethodsStage(LoadedWorkspace workspace)
    {
        Console.Write("Normalizing methods...");
        var timer = Stopwatch.StartNew();
        var normalizer = new IntentNormalizer();
        var normalized = normalizer.NormalizeAll(workspace);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return normalized;
    }

    public static List<(string MethodId, float[] Vector, string Model)> GenerateEmbeddingsStage(
        List<NormalizedMethod> normalized,
        IEmbeddingEngine engine,
        string engineName)
    {
        Console.Write($"Generating embeddings ({engineName})...");
        var timer = Stopwatch.StartNew();
        var modelLabel = engineName == "hash" ? "hash-v1" : engineName;
        var results = normalized
            .Select(n => (n.MethodId, Vector: engine.GenerateEmbedding(n.SemanticPayload), Model: modelLabel))
            .ToList();
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return results;
    }

    public static IEmbeddingEngine CreateEmbeddingEngine(string engineType, string? model, int dimensions, bool verbose)
    {
        switch (engineType.ToLower())
        {
            case "openai":
                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.Error.WriteLine("Warning: OPENAI_API_KEY not set, falling back to hash engine.");
                    return new HashEmbeddingEngine();
                }
                var modelName = string.IsNullOrEmpty(model) ? "text-embedding-3-small" : model;
                return new OpenAiEmbeddingEngine(apiKey, modelName, dimensions);

            case "onnx":
                var modelPath = model ?? "./models/all-MiniLM-L6-v2.onnx";
                if (!File.Exists(modelPath))
                {
                    Console.Error.WriteLine($"Warning: ONNX model not found at {modelPath}, falling back to hash engine.");
                    return new HashEmbeddingEngine();
                }
                return new OnnxEmbeddingEngine(modelPath, dimensions);

            default:
                return new HashEmbeddingEngine();
        }
    }

    public static async Task StoreResultsStage(
        StorageService storage,
        List<ExtractionResult> extractionResults,
        List<MethodCallEdge> edges,
        List<MethodMetrics> metrics,
        List<NormalizedMethod> normalized,
        List<(string MethodId, float[] Vector, string Model)> embeddings,
        CancellationToken ct)
    {
        Console.Write("Storing results...");
        var timer = Stopwatch.StartNew();
        await storage.InitializeAsync(ct);
        await storage.SaveCodeModelAsync(extractionResults, ct);
        await storage.SaveCallGraphAsync(
            edges.Select(e => (e.CallerId, e.CalleeId)).ToList(), ct);
        await storage.SaveMetricsAsync(
            metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList(), ct);
        await storage.SaveNormalizedMethodsAsync(
            normalized.Select(n => (n.MethodId, n.StructuralSignature, n.SemanticPayload)).ToList(), ct);
        await storage.SaveEmbeddingsAsync(embeddings, ct);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
    }

    public static async Task<(List<ClonePair> ClonePairs, List<IntentCluster> Clusters)> DetectDuplicatesStage(
        StorageService storage,
        List<NormalizedMethod> normalized,
        List<(string MethodId, float[] Vector, string Model)> embeddings,
        CancellationToken ct)
    {
        Console.Write("Detecting duplicates...");
        var timer = Stopwatch.StartNew();
        var structuralDetector = new StructuralCloneDetector();
        var semanticDetector = new SemanticCloneDetector();
        var hybridScorer = new HybridScorer();

        var structuralClones = structuralDetector.DetectClones(normalized);
        var embeddingPairs = embeddings.Select(e => (e.MethodId, e.Vector)).ToList();
        var semanticClones = semanticDetector.DetectClones(embeddingPairs);
        var clonePairs = hybridScorer.Merge(structuralClones, semanticClones);
        await storage.SaveClonePairsAsync(clonePairs, ct);

        var clusterer = new IntentClusterer();
        var clusters = clusterer.ClusterMethods(normalized, embeddingPairs);
        await storage.SaveClustersAsync(clusters, ct);
        Console.WriteLine($" done ({timer.Elapsed.TotalSeconds:F1}s)");
        return (clonePairs, clusters);
    }

    public static void SaveBaselineStage(string output, string dbPath)
    {
        var baselinePath = Path.Combine(output, "baseline.db");
        File.Copy(dbPath, baselinePath, overwrite: true);
        Console.WriteLine($"Baseline saved: {Path.GetFullPath(baselinePath)}");
    }

    public static void PrintAnalysisSummary(
        List<ExtractionResult> extractionResults,
        List<MethodCallEdge> edges,
        List<MethodMetrics> metrics,
        List<ClonePair> clonePairs,
        List<IntentCluster> clusters,
        Stopwatch totalTimer,
        string dbPath)
    {
        var totalProjects = extractionResults.Count;
        var totalTypes = extractionResults.Sum(r => ModelCountHelpers.CountTypes(r.Model));
        var totalMethods = extractionResults.Sum(r => ModelCountHelpers.CountMethods(r.Model));
        var avgComplexity = metrics.Count > 0 ? metrics.Average(m => m.CognitiveComplexity) : 0;

        Console.WriteLine();
        Console.WriteLine("Analysis complete:");
        Console.WriteLine($"  Projects:       {totalProjects:N0}");
        Console.WriteLine($"  Types:          {totalTypes:N0}");
        Console.WriteLine($"  Methods:        {totalMethods:N0}");
        Console.WriteLine($"  Call edges:     {edges.Count:N0}");
        Console.WriteLine($"  Clone pairs:    {clonePairs.Count:N0}");
        Console.WriteLine($"  Clusters:       {clusters.Count:N0}");
        Console.WriteLine($"  Avg complexity: {avgComplexity:F1}");
        Console.WriteLine($"  Duration:       {totalTimer.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Output:         {Path.GetFullPath(dbPath)}");
    }
}
