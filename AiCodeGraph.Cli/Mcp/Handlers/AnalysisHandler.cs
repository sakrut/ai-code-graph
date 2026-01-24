using System.Text.Json;
using System.Text.Json.Nodes;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class AnalysisHandler : IMcpToolHandler
{
    private readonly string _dbPath;
    private readonly Func<StorageService?> _getStorage;
    private readonly Action<StorageService?> _setStorage;
    private readonly Action _invalidateVectorIndex;

    public AnalysisHandler(string dbPath, Func<StorageService?> getStorage, Action<StorageService?> setStorage, Action invalidateVectorIndex)
    {
        _dbPath = dbPath;
        _getStorage = getStorage;
        _setStorage = setStorage;
        _invalidateVectorIndex = invalidateVectorIndex;
    }

    public IReadOnlyList<string> SupportedTools { get; } = new[]
    {
        "cg_analyze", "cg_churn", "cg_coupling", "cg_diff", "cg_get_drift"
    };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_analyze",
            "Analyze a .NET solution and build/rebuild the code graph database",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["solution"] = new JsonObject { ["type"] = "string", ["description"] = "Path to .sln file (auto-discovers if omitted)" },
                    ["save_baseline"] = new JsonObject { ["type"] = "boolean", ["description"] = "Save result as baseline for drift detection", ["default"] = false }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_churn",
            "Show methods with high change-frequency x complexity (churn hotspots)",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["since"] = new JsonObject { ["type"] = "string", ["description"] = "Git log time range (e.g. '6 months ago')", ["default"] = "6 months ago" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 20 }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_coupling",
            "Show afferent/efferent coupling and instability metrics",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["level"] = new JsonObject { ["type"] = "string", ["description"] = "namespace|type", ["default"] = "namespace" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 20 }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_diff",
            "Compare code between git refs, showing affected methods and complexity",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["from"] = new JsonObject { ["type"] = "string", ["description"] = "Base git ref", ["default"] = "HEAD~1" },
                    ["to"] = new JsonObject { ["type"] = "string", ["description"] = "Target git ref", ["default"] = "HEAD" },
                    ["format"] = new JsonObject { ["type"] = "string", ["description"] = "summary|detail|json", ["default"] = "summary" }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_drift",
            "Detect architectural drift from a baseline snapshot",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["baseline"] = new JsonObject { ["type"] = "string", ["description"] = "Path to baseline.db (default: auto-detect next to graph.db)" }
                }
            })
    };

    public Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct)
    {
        return toolName switch
        {
            "cg_analyze" => Analyze(args, ct),
            "cg_churn" => GetChurn(args, ct),
            "cg_coupling" => GetCoupling(args, ct),
            "cg_diff" => GetDiff(args, ct),
            "cg_get_drift" => GetDrift(args, ct),
            _ => Task.FromResult($"Unknown tool: {toolName}")
        };
    }

    private async Task<string> Analyze(JsonNode? args, CancellationToken ct)
    {
        var solutionPath = args?["solution"]?.GetValue<string>();
        var saveBaseline = args?["save_baseline"]?.GetValue<bool>() ?? false;

        try
        {
            var resolvedPath = SolutionDiscovery.FindSolutionFile(solutionPath);
            using var loader = new WorkspaceLoader();
            var workspace = await loader.LoadSolutionAsync(resolvedPath, null, ct);

            var extractor = new CodeModelExtractor();
            var extractionResults = new List<ExtractionResult>();
            foreach (var (projectId, compilation) in workspace.Compilations)
            {
                var project = workspace.Solution.GetProject(projectId);
                var result = extractor.ExtractProject(compilation, project?.Name ?? "", project?.FilePath ?? "");
                extractionResults.Add(result);
            }

            var callGraphBuilder = new CallGraphBuilder();
            var edges = callGraphBuilder.BuildCallGraph(workspace);

            var metricsEngine = new MetricsEngine();
            var metrics = metricsEngine.ComputeMetrics(workspace);

            var normalizer = new IntentNormalizer();
            var normalized = normalizer.NormalizeAll(workspace);

            using var embeddingEngine = new HashEmbeddingEngine();
            var embeddingResults = normalized
                .Select(n => (n.MethodId, Vector: embeddingEngine.GenerateEmbedding(n.SemanticPayload), Model: "hash-v1"))
                .ToList();

            var output = Path.GetDirectoryName(_dbPath) ?? "./ai-code-graph";
            Directory.CreateDirectory(output);

            // Close existing storage if open
            var existingStorage = _getStorage();
            if (existingStorage != null)
            {
                await existingStorage.DisposeAsync();
                _setStorage(null);
            }

            await using var storage = new StorageService(_dbPath);
            await storage.InitializeAsync(ct);
            await storage.SaveCodeModelAsync(extractionResults, ct);
            await storage.SaveCallGraphAsync(edges.Select(e => (e.CallerId, e.CalleeId)).ToList(), ct);
            await storage.SaveMetricsAsync(metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList(), ct);
            await storage.SaveNormalizedMethodsAsync(normalized.Select(n => (n.MethodId, n.StructuralSignature, n.SemanticPayload)).ToList(), ct);
            await storage.SaveEmbeddingsAsync(embeddingResults, ct);

            var structuralDetector = new StructuralCloneDetector();
            var semanticDetector = new SemanticCloneDetector();
            var hybridScorer = new HybridScorer();
            var structuralClones = structuralDetector.DetectClones(normalized);
            var embeddingPairs = embeddingResults.Select(e => (e.MethodId, e.Vector)).ToList();
            var semanticClones = semanticDetector.DetectClones(embeddingPairs);
            var clonePairs = hybridScorer.Merge(structuralClones, semanticClones);
            await storage.SaveClonePairsAsync(clonePairs, ct);

            var clusterer = new IntentClusterer();
            var clusters = clusterer.ClusterMethods(normalized, embeddingPairs);
            await storage.SaveClustersAsync(clusters, ct);

            if (saveBaseline)
            {
                var baselinePath = Path.Combine(output, "baseline.db");
                File.Copy(_dbPath, baselinePath, overwrite: true);
            }

            var totalMethods = extractionResults.Sum(r => r.Model.Namespaces.Sum(ns => McpProtocolHelpers.CountMethodsInNamespace(ns)));

            _invalidateVectorIndex();

            return string.Join("\n", new[]
            {
                "Analysis complete:",
                $"  Solution: {resolvedPath}",
                $"  Projects: {extractionResults.Count}",
                $"  Methods: {totalMethods}",
                $"  Call edges: {edges.Count}",
                $"  Clone pairs: {clonePairs.Count}",
                $"  Clusters: {clusters.Count}",
                $"  Output: {Path.GetFullPath(_dbPath)}",
                saveBaseline ? $"  Baseline saved: {Path.GetFullPath(Path.Combine(output, "baseline.db"))}" : ""
            }.Where(l => l.Length > 0));
        }
        catch (FileNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> GetChurn(JsonNode? args, CancellationToken ct)
    {
        var since = args?["since"]?.GetValue<string>() ?? "6 months ago";
        var top = args?["top"]?.GetValue<int>() ?? 20;

        var storage = _getStorage()!;
        var analyzer = new ChurnAnalyzer();
        var results = await analyzer.AnalyzeAsync(storage, since, top, ct);

        if (results.Count == 0)
            return "No churn hotspots found.";

        var lines = new List<string> { $"Churn hotspots (since: {since}):", "" };
        foreach (var r in results)
        {
            lines.Add($"  {r.MethodName}");
            lines.Add($"    Changes: {r.Changes}, CC: {r.CognitiveComplexity}, Score: {r.ChurnScore:F0}");
            if (r.FilePath != null) lines.Add($"    File: {r.FilePath}");
        }
        lines.Add($"\nTotal: {results.Count} methods");
        return string.Join("\n", lines);
    }

    private async Task<string> GetCoupling(JsonNode? args, CancellationToken ct)
    {
        var level = args?["level"]?.GetValue<string>() ?? "namespace";
        var top = args?["top"]?.GetValue<int>() ?? 20;

        var storage = _getStorage()!;
        var analyzer = new CouplingAnalyzer();
        var results = await analyzer.AnalyzeAsync(storage, level, ct);
        results = results.Take(top).ToList();

        if (results.Count == 0)
            return "No coupling data found.";

        var lines = new List<string> { $"Coupling metrics (level: {level}):", "" };
        lines.Add($"{"Name",-40} {"Ca",4} {"Ce",4} {"I",5} {"A",5} {"D",5}");
        lines.Add(new string('-', 66));
        foreach (var r in results)
        {
            var name = r.Name.Length > 38 ? r.Name[..35] + "..." : r.Name;
            lines.Add($"{name,-40} {r.AfferentCoupling,4} {r.EfferentCoupling,4} {r.Instability,5:F2} {r.Abstractness,5:F2} {r.DistanceFromMain,5:F2}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> GetDiff(JsonNode? args, CancellationToken ct)
    {
        var fromRef = args?["from"]?.GetValue<string>() ?? "HEAD~1";
        var toRef = args?["to"]?.GetValue<string>() ?? "HEAD";
        var format = args?["format"]?.GetValue<string>() ?? "summary";

        var changedFiles = await GetChangedCsFilesAsync(fromRef, toRef, ct);
        if (changedFiles.Count == 0)
            return $"No C# files changed between {fromRef}..{toRef}.";

        var storage = _getStorage()!;
        var allMethods = await storage.GetMethodsForExportAsync(null, ct);
        var changedFileNames = changedFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var affectedMethods = allMethods
            .Where(m => m.FilePath != null && changedFileNames.Contains(Path.GetFileName(m.FilePath)))
            .ToList();

        if (format == "json")
        {
            var json = JsonSerializer.Serialize(new
            {
                from = fromRef,
                to = toRef,
                filesChanged = changedFiles.Count,
                methodsAffected = affectedMethods.Count,
                files = changedFiles,
                methods = affectedMethods.Select(m => new
                {
                    id = m.Id,
                    name = m.FullName,
                    file = m.FilePath,
                    complexity = m.Complexity
                })
            }, new JsonSerializerOptions { WriteIndented = true });
            return json;
        }

        var lines = new List<string>
        {
            $"Changes between {fromRef}..{toRef}:",
            $"Files changed: {changedFiles.Count}",
            $"Methods affected: {affectedMethods.Count}",
            ""
        };

        if (format == "detail" && affectedMethods.Count > 0)
        {
            lines.Add($"{"Method",-50} {"File",-25} {"CC",4}");
            lines.Add(new string('-', 83));
            foreach (var m in affectedMethods.OrderByDescending(m => m.Complexity))
            {
                var name = m.FullName.Length > 48 ? m.FullName[..45] + "..." : m.FullName;
                var file = m.FilePath != null ? Path.GetFileName(m.FilePath) : "";
                lines.Add($"{name,-50} {file,-25} {m.Complexity,4}");
            }
        }
        else if (affectedMethods.Count > 0)
        {
            var highComplexity = affectedMethods.Where(m => m.Complexity > 10).ToList();
            if (highComplexity.Count > 0)
            {
                lines.Add($"High-complexity methods in changed files ({highComplexity.Count}):");
                foreach (var m in highComplexity.OrderByDescending(m => m.Complexity).Take(10))
                    lines.Add($"  {m.FullName} (CC={m.Complexity})");
            }
        }

        return string.Join("\n", lines);
    }

    private async Task<string> GetDrift(JsonNode? args, CancellationToken ct)
    {
        var baselinePath = args?["baseline"]?.GetValue<string>();
        if (string.IsNullOrEmpty(baselinePath))
            baselinePath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "baseline.db");

        if (!File.Exists(baselinePath))
            return $"Baseline not found at {baselinePath}. Run 'ai-code-graph analyze --save-baseline' first.";

        var detector = new AiCodeGraph.Core.Drift.DriftDetector();
        var report = await detector.CompareAsync(_dbPath, baselinePath, ct);

        var hasDrift = report.NewMethods.Count > 0 || report.RemovedMethods.Count > 0
            || report.Regressions.Count > 0 || report.NewDuplicates.Count > 0
            || report.IntentScattering.Count > 0;

        if (!hasDrift) return "No drift detected.";

        var lines = new List<string>();

        if (report.NewMethods.Count > 0)
        {
            lines.Add($"New Methods ({report.NewMethods.Count}):");
            foreach (var m in report.NewMethods.Take(10))
                lines.Add($"  + {m.FullName}");
            if (report.NewMethods.Count > 10)
                lines.Add($"  ... and {report.NewMethods.Count - 10} more");
            lines.Add("");
        }

        if (report.RemovedMethods.Count > 0)
        {
            lines.Add($"Removed Methods ({report.RemovedMethods.Count}):");
            foreach (var m in report.RemovedMethods.Take(10))
                lines.Add($"  - {m.FullName}");
            if (report.RemovedMethods.Count > 10)
                lines.Add($"  ... and {report.RemovedMethods.Count - 10} more");
            lines.Add("");
        }

        if (report.Regressions.Count > 0)
        {
            lines.Add($"Complexity Regressions ({report.Regressions.Count}):");
            foreach (var r in report.Regressions.Take(10))
                lines.Add($"  {r.FullName}: {r.BaselineComplexity} -> {r.CurrentComplexity} (+{(r.PercentageIncrease * 100):F0}%)");
            lines.Add("");
        }

        if (report.NewDuplicates.Count > 0)
        {
            lines.Add($"New Duplicates ({report.NewDuplicates.Count}):");
            foreach (var d in report.NewDuplicates.Take(10))
                lines.Add($"  {d.MethodIdA} <-> {d.MethodIdB} ({d.HybridScore:F2})");
            lines.Add("");
        }

        if (report.IntentScattering.Count > 0)
        {
            lines.Add($"Intent Scattering ({report.IntentScattering.Count}):");
            foreach (var s in report.IntentScattering.Take(5))
                lines.Add($"  '{s.ClusterLabel}' spread to: {string.Join(", ", s.NewNamespaces)}");
        }

        return string.Join("\n", lines);
    }

    private static async Task<List<string>> GetChangedCsFilesAsync(string fromRef, string toRef, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"diff --name-only {fromRef} {toRef} -- \"*.cs\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return new List<string>();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0) return new List<string>();

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
